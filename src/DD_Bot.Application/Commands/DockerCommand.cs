using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;
using Docker.DotNet.Models;
using DD_Bot.Domain;
using DD_Bot.Application.Services;
using Microsoft.Extensions.Logging;

namespace DD_Bot.Application.Commands
{
    public class DockerCommand
    {
        private readonly DiscordSocketClient _discord;
        private readonly DockerService _dockerService;
        private readonly DiscordSettings _settings;
        private readonly ILogger<DockerCommand> _logger;

        // Command constants
        private const string StartCommand = "start";
        private const string StopCommand = "stop";
        private const string RestartCommand = "restart";

        public DockerCommand(DiscordSocketClient discord, DockerService dockerService, DiscordSettings settings, ILogger<DockerCommand> logger)
        {
            _discord = discord;
            _dockerService = dockerService;
            _settings = settings;
            _logger = logger;
        }

        #region Command Initialization

        public async Task InitializeCommands()
        {
            try
            {
                var commandProps = CreateCommand();

                // Retrieve the GUILD_ID from the environment variable
                var guildIdEnv = Environment.GetEnvironmentVariable("GUILD_ID");
                if (string.IsNullOrEmpty(guildIdEnv) || !ulong.TryParse(guildIdEnv, out var guildId))
                {
                    _logger.LogError("GUILD_ID environment variable is not set or invalid.");
                    return;
                }

                // Register the command as a guild-specific command
                var guild = _discord.GetGuild(guildId);
                if (guild == null)
                {
                    _logger.LogError("Guild with ID {GuildId} not found.", guildId);
                    return;
                }

                await guild.CreateApplicationCommandAsync(commandProps);
                _logger.LogDebug("Guild-specific commands registered for Guild ID {GuildId}.", guildId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"InitializeCommands Exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static ApplicationCommandProperties CreateCommand()
        {
            return new SlashCommandBuilder()
            {
                Name = "docker",
                Description = "Execute a command on a Docker container"
            }
            .AddOption("command", ApplicationCommandOptionType.String, "Choose a command", true, choices: new[]
            {
                new ApplicationCommandOptionChoiceProperties { Name = "Start", Value = StartCommand },
                new ApplicationCommandOptionChoiceProperties { Name = "Stop", Value = StopCommand },
                new ApplicationCommandOptionChoiceProperties { Name = "Restart", Value = RestartCommand }
            })
            .Build();
        }

        #endregion

        #region Command Handlers

        public async Task HandleSlashCommand(SocketSlashCommand command, DockerService dockerService, DiscordSettings settings)
        {
            try
            {
                var selectedCommand = command.Data.Options.First().Value.ToString();
                var user = command.User as SocketGuildUser;

                var sections = GetAccessibleSections(user);
                if (!sections.Any())
                {
                    await command.RespondAsync("You have no access to any sections.", ephemeral: true);
                    return;
                }

                var selectMenu = BuildSelectMenu("section_select", sections, "Choose a section");
                await command.RespondAsync("Please select a section:", components: selectMenu, ephemeral: true);
            }
            catch (Exception ex)
            {
                _logger.LogError($"HandleSlashCommand Exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public async Task HandleSectionSelect(SocketMessageComponent component, DockerService dockerService, DiscordSettings settings)
        {
            try
            {
                _logger.LogDebug("HandleSectionSelect invoked by user {UserId} with component ID {ComponentId}.", component.User.Id, component.Data.CustomId);

                // Defer the response immediately to avoid interaction timeout
                await component.DeferAsync();
                _logger.LogDebug("Response deferred for component ID {ComponentId}.", component.Data.CustomId);

                // Extract the selected section
                var selectedSection = component.Data.Values.First();
                _logger.LogDebug("User {UserId} selected section: {SelectedSection}.", component.User.Id, selectedSection);

                // Get valid containers for the selected section
                var validContainers = GetContainersBySection(selectedSection);
                _logger.LogDebug("Found {ContainerCount} containers in section {SelectedSection}.", validContainers.Count, selectedSection);

                // If no containers are found, send a follow-up response
                if (!validContainers.Any())
                {
                    _logger.LogWarning("No containers available in section {SelectedSection} for user {UserId}.", selectedSection, component.User.Id);
                    await component.FollowupAsync("No containers available in this section.", ephemeral: true);
                    return;
                }

                // Build the select menu for containers
                var selectMenu = BuildSelectMenu($"container_select:{selectedSection}", validContainers, "Choose a container");
                _logger.LogDebug("Select menu built for section {SelectedSection} with {OptionCount} options.", selectedSection, validContainers.Count);

                // Send the follow-up response with the select menu
                await component.FollowupAsync("Please select a container:", components: selectMenu, ephemeral: true);
                _logger.LogDebug("Followup message sent for section {SelectedSection} to user {UserId}.", selectedSection, component.User.Id);
            }
            catch (Exception ex)
            {
                // Log the exception and send an error response
                _logger.LogError(ex, "HandleSectionSelect Exception for user {UserId} with component ID {ComponentId}.", component.User.Id, component.Data.CustomId);
                try
                {
                    await component.FollowupAsync("An error occurred while processing your request.", ephemeral: true);
                }
                catch (Exception followupEx)
                {
                    _logger.LogError(followupEx, "Failed to send follow-up error message for user {UserId} with component ID {ComponentId}.", component.User.Id, component.Data.CustomId);
                }
            }
        }

        public async Task HandleContainerSelect(SocketMessageComponent component, DockerService dockerService, DiscordSettings settings)
        {
            try
            {
                _logger.LogDebug("HandleContainerSelect invoked by user {UserId} with component ID {ComponentId}.", component.User.Id, component.Data.CustomId);

                var containerName = component.Data.Values.First();
                _logger.LogDebug("User {UserId} selected container: {ContainerName}.", component.User.Id, containerName);

                var command = component.Data.CustomId.Split(':')[1];
                _logger.LogDebug("Command extracted from component ID {ComponentId}: {Command}.", component.Data.CustomId, command);

                await ExecuteDockerCommand(command, containerName, component.User.Id);
                _logger.LogDebug("Executed command {Command} on container {ContainerName} for user {UserId}.", command, containerName, component.User.Id);

                await component.ModifyOriginalResponseAsync(msg => msg.Content = $"Successfully executed {command} on `{containerName}`.");
                _logger.LogDebug("Response modified for user {UserId} after executing command {Command} on container {ContainerName}.", component.User.Id, command, containerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleContainerSelect Exception for user {UserId} with component ID {ComponentId}.", component.User.Id, component.Data.CustomId);
                await component.FollowupAsync("An error occurred while processing your request.", ephemeral: true);
            }
        }

        #endregion

        #region Docker Command Execution

        private async Task ExecuteDockerCommand(string command, string containerName, ulong userId)
        {
            var docker = _dockerService.DockerStatus.FirstOrDefault(d => d.Names[0] == containerName);
            if (docker == null)
            {
                _logger.LogError($"Container not found: {containerName}");
                return;
            }

            var dockerId = docker.ID;
            switch (command)
            {
                case StartCommand:
                    await _dockerService.DockerCommandStart(dockerId, userId);
                    break;
                case StopCommand:
                    await _dockerService.DockerCommandStop(dockerId, userId);
                    break;
                case RestartCommand:
                    await _dockerService.DockerCommandRestart(dockerId, userId);
                    break;
                default:
                    _logger.LogError($"Unknown command: {command}");
                    break;
            }
        }

        #endregion

        #region Helpers

        public List<string> GetSectionsForUser(SocketGuildUser user)
        {
            return GetAccessibleSections(user);
        }

        private List<string> GetAccessibleSections(SocketGuildUser user)
        {
            var sections = new HashSet<string>();
            if (_settings.AdminIDs.Contains(user.Id))
                return _settings.SectionOrder;

            foreach (var role in user.Roles)
            {
                if (_settings.RoleStartPermissions.TryGetValue(role.Id, out var startSections))
                    sections.UnionWith(startSections);
                if (_settings.RoleStopPermissions.TryGetValue(role.Id, out var stopSections))
                    sections.UnionWith(stopSections);
            }

            return sections.ToList();
        }

        private List<string> GetContainersBySection(string section)
        {
            return _dockerService.DockerStatus
                .Where(c => c.Labels.TryGetValue("section", out var sec) && sec == section)
                .Select(c => c.Names[0])
                .ToList();
        }

        private MessageComponent BuildSelectMenu(string customId, IEnumerable<string> options, string placeholder)
        {
            var selectMenu = new SelectMenuBuilder()
                .WithCustomId(customId)
                .WithPlaceholder(placeholder);

            foreach (var option in options)
                selectMenu.AddOption(option, option);

            return new ComponentBuilder().WithSelectMenu(selectMenu).Build();
        }

        #endregion
    }
}
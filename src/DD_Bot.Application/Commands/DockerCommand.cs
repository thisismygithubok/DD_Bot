/* DD_Bot - A Discord Bot to control Docker containers*/

 /*  Copyright (C) 2022 Maxim Kovac
     This program is free software: you can redistribute it and/or modify
     it under the terms of the GNU General Public License as published by
     the Free Software Foundation, either version 3 of the License, or
     (at your option) any later version.
     This program is distributed in the hope that it will be useful,
     but WITHOUT ANY WARRANTY; without even the implied warranty of
     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
     GNU General Public License for more details.
     You should have received a copy of the GNU General Public License
     along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Discord.Interactions;
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

        public DockerCommand(DiscordSocketClient discord, DockerService dockerService, DiscordSettings settings, ILogger<DockerCommand> logger)
        {
            _discord = discord;
            _dockerService = dockerService;
            _settings = settings;
            _logger = logger;
        }

        public async Task InitializeCommands()
        {
            try
            {
                var commandProps = DockerCommand.Create();
                
                // Register commands globally
                await _discord.CreateGlobalApplicationCommandAsync(commandProps);
                _logger.LogInformation("Global commands registered.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"InitializeCommands Exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static ApplicationCommandProperties Create()
        {
            var builder = new SlashCommandBuilder()
            {
                Name = "docker",
                Description = "Execute a command on a Docker container"
            };

            builder.AddOption(new SlashCommandOptionBuilder()
                .WithName("command")
                .WithDescription("Choose a command")
                .WithRequired(true)
                .WithType(ApplicationCommandOptionType.String)
                .AddChoice("Start", "start")
                .AddChoice("Stop", "stop")
                .AddChoice("Restart", "restart")
            );

            return builder.Build();
        }

        public List<string> GetSectionsForUser(DiscordSettings settings, IReadOnlyCollection<SocketRole> roles, ulong userId)
        {
            var sections = new HashSet<string>();

            // **Grant access to all sections if the user is an admin**
            if (settings.AdminIDs.Contains(userId))
            {
                return settings.SectionOrder;
            }

            // Existing logic to get sections based on roles
            foreach (var role in roles)
            {
                if (settings.RoleStartPermissions.ContainsKey(role.Id))
                {
                    sections.UnionWith(settings.RoleStartPermissions[role.Id]);
                }
                if (settings.RoleStopPermissions.ContainsKey(role.Id))
                {
                    sections.UnionWith(settings.RoleStopPermissions[role.Id]);
                }
            }

            return sections.ToList();
        }

        public async Task HandleSectionSelect(SocketMessageComponent component, DockerService dockerService, DiscordSettings settings)
        {
            try
            {
                _logger.LogDebug("HandleSectionSelect: Entered method");

                var selectedSection = component.Data.Values.First();
                var commandFromCustomId = component.Data.CustomId.Split(':')[1]; // Extract command from CustomId
                _logger.LogDebug($"HandleSectionSelect: Selected section - {selectedSection}, Command - {commandFromCustomId}");

                var socketUser = component.User as SocketGuildUser;
                var userRoles = socketUser.Roles;

                var validContainers = GetValidContainersForUser(dockerService, settings, selectedSection, component.User.Id, userRoles);

                if (!validContainers.Any())
                {
                    await component.RespondAsync("You have no access to containers in this section.", ephemeral: true);
                    _logger.LogDebug("HandleSectionSelect: User has no valid containers.");
                    return;
                }

                var selectMenu = new SelectMenuBuilder()
                    .WithPlaceholder("Choose a container")
                    .WithCustomId($"container_select:{commandFromCustomId}:{selectedSection}"); // Embed command and section in CustomId

                foreach (var container in validContainers)
                {
                    selectMenu.AddOption(container.Names[0], container.Names[0]);
                }

                var componentBuilder = new ComponentBuilder()
                    .WithSelectMenu(selectMenu);

                await component.RespondAsync("Please select a container:", components: componentBuilder.Build(), ephemeral: true);

                _logger.LogDebug("HandleSectionSelect: Finished");
            }
            catch (Exception ex)
            {
                _logger.LogError($"HandleSectionSelect: Exception - {ex.Message}\n{ex.StackTrace}");
            }
        }

        private List<ContainerListResponse> GetValidContainersForUser(DockerService dockerService, DiscordSettings settings, string section, ulong userId, IReadOnlyCollection<SocketRole> userRoles)
        {
            var containers = dockerService.DockerStatus;

            // Filter containers by section label
            var containersInSection = containers
                .Where(c => c.Labels != null && c.Labels.ContainsKey("section") && c.Labels["section"] == section)
                .ToList();

            // **Grant access to all containers if the user is an admin**
            if (settings.AdminIDs.Contains(userId))
            {
                return containersInSection;
            }

            var validContainers = new List<ContainerListResponse>();

            foreach (var container in containersInSection)
            {
                bool authorized = false;
                var containerName = container.Names[0];

                // Check user permissions
                if (settings.UserStartPermissions.ContainsKey(userId) && settings.UserStartPermissions[userId].Contains(containerName))
                {
                    authorized = true;
                }

                // Check role permissions
                foreach (var role in userRoles)
                {
                    if (settings.RoleStartPermissions.ContainsKey(role.Id) && settings.RoleStartPermissions[role.Id].Contains(section))
                    {
                        authorized = true;
                        break;
                    }
                    if (settings.RoleStopPermissions.ContainsKey(role.Id) && settings.RoleStopPermissions[role.Id].Contains(section))
                    {
                        authorized = true;
                        break;
                    }
                }

                if (authorized)
                {
                    validContainers.Add(container);
                }
            }

            return validContainers;
        }

        public async Task HandleContainerSelect(SocketMessageComponent component, DockerService dockerService, DiscordSettings settings)
        {
            try
            {
                _logger.LogDebug("HandleContainerSelect: Entered method");

                var containerName = component.Data.Values.First();
                var ids = component.Data.CustomId.Split(':');
                var selectedCommand = ids[1];
                var selectedSection = ids[2];
                _logger.LogDebug($"HandleContainerSelect: Selected container - {containerName}, Command - {selectedCommand}, Section - {selectedSection}");

                var commandArgs = new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("command", selectedCommand),
                    new KeyValuePair<string, object>("dockername", containerName),
                };

                var context = new SocketInteractionContext<SocketMessageComponent>(_discord, component);

                await component.RespondAsync($"Processing your request to {selectedCommand} `{containerName}`...");

                _logger.LogDebug("HandleContainerSelect: About to call ExecuteInternal");

                await ExecuteInternal(context, commandArgs, settings);

                _logger.LogDebug("HandleContainerSelect: Finished ExecuteInternal");
            }
            catch (Exception ex)
            {
                _logger.LogError($"HandleContainerSelect: Exception - {ex.Message}\n{ex.StackTrace}");
            }
        }

        public async Task HandleSlashCommand(SocketSlashCommand command, DockerService dockerService, DiscordSettings settings)
        {
            try
            {
                _logger.LogDebug("HandleSlashCommand: Entered method");

                // Get the selected command
                var selectedCommand = command.Data.Options.First().Value.ToString();
                _logger.LogDebug($"HandleSlashCommand: Selected command - {selectedCommand}");

                var socketUser = command.User as SocketGuildUser;
                var userRoles = socketUser.Roles;
                var userId = command.User.Id;

                // Get valid sections for the user
                var sections = GetSectionsForUser(settings, userRoles, userId);

                if (!sections.Any())
                {
                    await command.RespondAsync("You have no access to any sections.", ephemeral: true);
                    _logger.LogDebug("HandleSlashCommand: User has no valid sections.");
                    return;
                }

                var selectMenu = new SelectMenuBuilder()
                    .WithPlaceholder("Choose a section")
                    .WithCustomId($"section_select:{selectedCommand}"); // Embed selected command in CustomId

                foreach (var section in sections)
                {
                    selectMenu.AddOption(section, section);
                }

                var component = new ComponentBuilder()
                    .WithSelectMenu(selectMenu)
                    .Build();

                await command.RespondAsync("Please select a section:", components: component, ephemeral: true);

                _logger.LogDebug("HandleSlashCommand: Finished");
            }
            catch (Exception ex)
            {
                _logger.LogError($"HandleSlashCommand: Exception - {ex.Message}\n{ex.StackTrace}");
            }
        }

        public async Task HandleCommandSelect(SocketMessageComponent component, DockerService dockerService, DiscordSettings settings)
        {
            try
            {
                _logger.LogDebug("HandleCommandSelect: Entered method");

                var command = component.Data.Values.First();
                _logger.LogDebug($"HandleCommandSelect: Selected command - {command}");

                var containerName = ExtractContainerNameFromMessage(component.Message.Content);
                _logger.LogDebug($"HandleCommandSelect: Extracted container name - {containerName}");

                var commandArgs = new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("command", command),
                    new KeyValuePair<string, object>("dockername", containerName),
                };

                var context = new SocketInteractionContext<SocketMessageComponent>(_discord, component);

                await component.RespondAsync("Processing your request...");

                _logger.LogDebug("HandleCommandSelect: About to call ExecuteInternal");

                await ExecuteInternal(context, commandArgs, settings);

                _logger.LogDebug("HandleCommandSelect: Finished ExecuteInternal");
            }
            catch (Exception ex)
            {
                _logger.LogError($"HandleCommandSelect: Exception - {ex.Message}\n{ex.StackTrace}");
            }
        }

        private string ExtractContainerNameFromMessage(string messageContent)
        {
            // Assume the message contains "Please select a command for container: `containerName`"
            var pattern = @"`([^`]+)`";
            var match = System.Text.RegularExpressions.Regex.Match(messageContent, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return string.Empty;
        }

        private async Task ExecuteInternal<T>(SocketInteractionContext<T> context, List<KeyValuePair<string, object>> commandArgs, DiscordSettings settings) where T : SocketInteraction
        {
            _logger.LogDebug("ExecuteInternal: Entered method");

            var command = commandArgs.First(arg => arg.Key == "command").Value as string;
            var dockerName = commandArgs.First(arg => arg.Key == "dockername").Value as string;

            _logger.LogDebug($"ExecuteInternal: Command - {command}, Docker Name - {dockerName}");

            await _dockerService.DockerUpdate();
            _logger.LogDebug("ExecuteInternal: DockerUpdate called");

            bool authorized = true;

            if (!settings.AdminIDs.Contains(context.User.Id))
            {
                authorized = false;
                var socketUser = context.User as SocketGuildUser;
                var guild = socketUser.Guild;
                var userRoles = guild.GetUser(socketUser.Id).Roles;

                _logger.LogDebug("ExecuteInternal: Checking user permissions");

                switch (command)
                {
                    case "start":
                        if (settings.UserStartPermissions.ContainsKey(context.User.Id))
                        {
                            if (settings.UserStartPermissions[context.User.Id].Contains(dockerName))
                            {
                                authorized = true;
                            }
                        }
                        foreach (var role in userRoles)
                        {
                            if (settings.RoleStartPermissions.ContainsKey(role.Id))
                            {
                                if (settings.RoleStartPermissions[role.Id].Contains(dockerName))
                                {
                                    authorized = true;
                                }
                            }
                        }
                        break;
                    case "stop":
                    case "restart":
                        if (settings.UserStopPermissions.ContainsKey(context.User.Id))
                        {
                            if (settings.UserStopPermissions[context.User.Id].Contains(dockerName))
                            {
                                authorized = true;
                            }
                        }
                        foreach (var role in userRoles)
                        {
                            if (settings.RoleStopPermissions.ContainsKey(role.Id))
                            {
                                if (settings.RoleStopPermissions[role.Id].Contains(dockerName))
                                {
                                    authorized = true;
                                }
                            }
                        }
                        break;
                }

                if (!authorized)
                {
                    _logger.LogError("ExecuteInternal: User not authorized");
                    await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = "You are not allowed to use this command");
                    return;
                }
            }

            if (string.IsNullOrEmpty(dockerName))
            {
                _logger.LogError("ExecuteInternal: No container name specified");
                await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = "No container name has been specified");
                return;
            }

            var docker = _dockerService.DockerStatus.FirstOrDefault(d => d.Names[0] == dockerName);

            if (docker == null)
            {
                _logger.LogError("ExecuteInternal: Docker container not found");
                await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = "Container with the name ***" + dockerName + "*** doesn't exist");
                return;
            }

            var dockerId = docker.ID;

            _logger.LogDebug($"ExecuteInternal: Docker ID - {dockerId}");

            switch (command)
            {
                case "start":
                    _logger.LogDebug("ExecuteInternal: Command is start");
                    if (_dockerService.RunningDockers.Contains(dockerName))
                    {
                        _logger.LogDebug("ExecuteInternal: Docker container already running");
                        await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = dockerName + " is already running");
                        return;
                    }
                    _logger.LogDebug("ExecuteInternal: Calling DockerCommandStart");
                    await _dockerService.DockerCommandStart(dockerId);
                    _logger.LogDebug("ExecuteInternal: DockerCommandStart completed");
                    break;
                case "stop":
                    _logger.LogDebug("ExecuteInternal: Command is stop");
                    _logger.LogDebug("ExecuteInternal: Calling DockerCommandStop");
                    await _dockerService.DockerCommandStop(dockerId);
                    _logger.LogDebug("ExecuteInternal: DockerCommandStop completed");
                    break;
                case "restart":
                    _logger.LogDebug("ExecuteInternal: Command is restart");
                    _logger.LogDebug("ExecuteInternal: Calling DockerCommandStop");
                    await _dockerService.DockerCommandStop(dockerId);
                    _logger.LogDebug("ExecuteInternal: DockerCommandStop completed");
                    _logger.LogDebug("ExecuteInternal: Calling DockerCommandStart");
                    await _dockerService.DockerCommandStart(dockerId);
                    _logger.LogDebug("ExecuteInternal: DockerCommandStart completed");
                    break;
            }

            for (int i = 0; i < _dockerService.Settings.Retries; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(_dockerService.Settings.TimeBeforeRetry));
                await _dockerService.DockerUpdate();

                switch (command)
                {
                    case "start":
                        if (_dockerService.RunningDockers.Contains(dockerName))
                        {
                            _logger.LogDebug("ExecuteInternal: Docker container started");
                            try
                            {
                                await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = context.User.Mention + " " + dockerName + " has been started");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"ExecuteInternal: Exception occurred while responding - {ex.Message}");
                            }
                            return;
                        }
                        break;
                    case "stop":
                        if (_dockerService.StoppedDockers.Contains(dockerName))
                        {
                            _logger.LogDebug("ExecuteInternal: Docker container stopped");
                            try
                            {
                                await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = context.User.Mention + " " + dockerName + " has been stopped");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"ExecuteInternal: Exception occurred while responding - {ex.Message}");
                            }
                            return;
                        }
                        break;
                    case "restart":
                        if (_dockerService.RunningDockers.Contains(dockerName))
                        {
                            _logger.LogDebug("ExecuteInternal: Docker container restarted");
                            try
                            {
                                await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = context.User.Mention + " " + dockerName + " has been restarted");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"ExecuteInternal: Exception occurred while responding - {ex.Message}");
                            }
                            return;
                        }
                        break;
                }
            }

            await _dockerService.DockerUpdate();

            switch (command)
            {
                case "start":
                    if (_dockerService.RunningDockers.Contains(dockerName))
                    {
                        _logger.LogDebug("ExecuteInternal: Docker container started after retries");
                        try
                        {
                            await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = context.User.Mention + " " + dockerName + " has been started");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"ExecuteInternal: Exception occurred while responding - {ex.Message}");
                        }
                        return;
                    }
                    _logger.LogError("ExecuteInternal: Docker container could not be started after retries");
                    try
                    {
                        await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = context.User.Mention + " " + dockerName + " could not be started");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"ExecuteInternal: Exception occurred while responding - {ex.Message}");
                    }
                    break;
                case "stop":
                    if (_dockerService.StoppedDockers.Contains(dockerName))
                    {
                        _logger.LogDebug("ExecuteInternal: Docker container stopped after retries");
                        try
                        {
                            await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = context.User.Mention + " " + dockerName + " has been stopped");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"ExecuteInternal: Exception occurred while responding - {ex.Message}");
                        }
                        return;
                    }
                    _logger.LogError("ExecuteInternal: Docker container could not be stopped after retries");
                    try
                    {
                        await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = context.User.Mention + " " + dockerName + " could not be stopped");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"ExecuteInternal: Exception occurred while responding - {ex.Message}");
                    }
                    break;
                case "restart":
                    if (_dockerService.RunningDockers.Contains(dockerName))
                    {
                        _logger.LogDebug("ExecuteInternal: Docker container restarted after retries");
                        try
                        {
                            await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = context.User.Mention + " " + dockerName + " has been restarted");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"ExecuteInternal: Exception occurred while responding - {ex.Message}");
                        }
                        return;
                    }
                    _logger.LogError("ExecuteInternal: Docker container could not be restarted after retries");
                    try
                    {
                        await context.Interaction.ModifyOriginalResponseAsync(edit => edit.Content = context.User.Mention + " " + dockerName + " could not be restarted");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"ExecuteInternal: Exception occurred while responding - {ex.Message}");
                    }
                    break;
            }
        }
    }
}

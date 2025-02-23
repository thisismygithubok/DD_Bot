using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using DD_Bot.Domain;
using DD_Bot.Application.Services;

namespace DD_Bot.Application.Commands
{
    public class DockerCommand
    {
        private readonly DiscordSocketClient _discord;
        private readonly DockerService _dockerService;

        public DockerCommand(DiscordSocketClient discord, DockerService dockerService)
        {
            _discord = discord;
            _dockerService = dockerService;
        }

        #region CreateCommand

        public static ApplicationCommandProperties Create(List<string> sections)
        {
            var builder = new SlashCommandBuilder()
            {
                Name = "docker",
                Description = "Issue a command to Docker"
            };

            builder.AddOption("section",
                ApplicationCommandOptionType.String,
                "choose a section",
                true,
                choices: sections.Select(section => new ApplicationCommandOptionChoiceProperties
                {
                    Name = section,
                    Value = section
                }).ToArray());

            builder.AddOption("dockername",
                ApplicationCommandOptionType.String,
                "choose a container",
                true);

            builder.AddOption("command",
                ApplicationCommandOptionType.String,
                "choose a command",
                true,
                choices: new[]
                {
                    new ApplicationCommandOptionChoiceProperties()
                    {
                        Name = "start",
                        Value = "start",
                    },
                    new ApplicationCommandOptionChoiceProperties()
                    {
                        Name = "stop",
                        Value = "stop",
                    },
                    new ApplicationCommandOptionChoiceProperties()
                    {
                        Name = "restart",
                        Value = "restart",
                    },
                });

            return builder.Build();
        }

        #endregion

        public List<string> GetSectionsForRoles(DiscordSettings settings, IReadOnlyCollection<SocketRole> roles)
        {
            var sections = new HashSet<string>();
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

        #region InteractiveComponents

        public async Task HandleSectionSelect(SocketMessageComponent component, DockerService dockerService, DiscordSettings settings)
        {
            var section = component.Data.Values.First();
            var socketUser = component.User as SocketGuildUser;
            var userRoles = socketUser.Roles;

            var validContainers = ValidateSectionLabels(dockerService, settings, userRoles)
                                  .Where(c => c.Contains(section))
                                  .ToList();

            var containerDropdown = new SelectMenuBuilder()
                .WithPlaceholder("Select a container")
                .WithCustomId("container_select");

            validContainers.ForEach(container =>
                containerDropdown.AddOption(new SelectMenuOptionBuilder().WithLabel(container).WithValue(container))
            );

            var builder = new ComponentBuilder().WithSelectMenu(containerDropdown);

            await component.RespondAsync("Please select a container:", components: builder.Build());
        }

        public async Task HandleContainerSelect(SocketMessageComponent component, DockerService dockerService, DiscordSettings settings)
        {
            var containerName = component.Data.Values.First();

            var commandDropdown = new SelectMenuBuilder()
                .WithPlaceholder("Select a command")
                .WithCustomId("command_select")
                .AddOption(new SelectMenuOptionBuilder().WithLabel("start").WithValue("start"))
                .AddOption(new SelectMenuOptionBuilder().WithLabel("stop").WithValue("stop"))
                .AddOption(new SelectMenuOptionBuilder().WithLabel("restart").WithValue("restart"));

            var builder = new ComponentBuilder().WithSelectMenu(commandDropdown);

            await component.RespondAsync($"Container `{containerName}` selected. Please choose a command:", components: builder.Build());
        }

        #endregion

        #region ExecuteCommand

        public async Task HandleSlashCommand(SocketSlashCommand command, DockerService dockerService, DiscordSettings settings)
        {
            await command.RespondAsync("Processing your request...");

            var commandArgs = command.Data.Options
                .Select(option => new KeyValuePair<string, object>(option.Name, option.Value))
                .ToList();

            var context = new SocketInteractionContext<SocketSlashCommand>(_discord, command);

            Console.WriteLine("HandleSlashCommand: About to call ExecuteInternal");

            await ExecuteInternal(context, commandArgs, settings);

            Console.WriteLine("HandleSlashCommand: Finished ExecuteInternal");
        }

        public async Task HandleCommandSelect(SocketMessageComponent component, DockerService dockerService, DiscordSettings settings)
        {
            var command = component.Data.Values.First();
            var containerName = component.Message.Content.Split('`')[1];

            var commandArgs = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("command", command),
                new KeyValuePair<string, object>("dockername", containerName),
            };

            var context = new SocketInteractionContext<SocketMessageComponent>(_discord, component);

            await component.RespondAsync("Processing your request...");

            Console.WriteLine("HandleCommandSelect: About to call ExecuteInternal");

            await ExecuteInternal(context, commandArgs, settings);

            Console.WriteLine("HandleCommandSelect: Finished ExecuteInternal");
        }

        private async Task ExecuteInternal<T>(SocketInteractionContext<T> context, List<KeyValuePair<string, object>> commandArgs, DiscordSettings settings) where T : SocketInteraction
        {
            Console.WriteLine("ExecuteInternal: Entered method");

            var command = commandArgs.First(arg => arg.Key == "command").Value as string;
            var dockerName = commandArgs.First(arg => arg.Key == "dockername").Value as string;

            Console.WriteLine($"ExecuteInternal: Command - {command}, Docker Name - {dockerName}");

            await _dockerService.DockerUpdate();
            Console.WriteLine("ExecuteInternal: DockerUpdate called");

            bool authorized = true;

            if (!settings.AdminIDs.Contains(context.User.Id))
            {
                authorized = false;
                var socketUser = context.User as SocketGuildUser;
                var guild = socketUser.Guild;
                var userRoles = guild.GetUser(socketUser.Id).Roles;

                Console.WriteLine("ExecuteInternal: Checking user permissions");

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
                    await context.Interaction.RespondAsync("You are not allowed to use this command");
                    Console.WriteLine("ExecuteInternal: User not authorized");
                    return;
                }
            }

            if (string.IsNullOrEmpty(dockerName))
            {
                await context.Interaction.RespondAsync("No container name has been specified");
                Console.WriteLine("ExecuteInternal: No container name specified");
                return;
            }

            var docker = _dockerService.DockerStatus.FirstOrDefault(d => d.Names[0] == dockerName);

            if (docker == null)
            {
                await context.Interaction.RespondAsync("Container with the name ***" + dockerName + "*** doesn't exist");
                Console.WriteLine("ExecuteInternal: Docker container not found");
                return;
            }

            var dockerId = docker.ID;

            Console.WriteLine($"ExecuteInternal: Docker ID - {dockerId}");

            switch (command)
            {
                case "start":
                    Console.WriteLine("ExecuteInternal: Command is start");
                    if (_dockerService.RunningDockers.Contains(dockerName))
                    {
                        await context.Interaction.RespondAsync(dockerName + " is already running");
                        Console.WriteLine("ExecuteInternal: Docker container already running");
                        return;
                    }
                    await _dockerService.DockerCommandStart(dockerId);
                    Console.WriteLine("ExecuteInternal: DockerCommandStart called");
                    break;
                case "stop":
                case "restart":
                    Console.WriteLine($"ExecuteInternal: Command is {command}");
                    if (_dockerService.StoppedDockers.Contains(dockerName))
                    {
                        await context.Interaction.RespondAsync(dockerName + " is already stopped");
                        Console.WriteLine("ExecuteInternal: Docker container already stopped");
                        return;
                    }
                    await _dockerService.DockerCommandStop(dockerId);
                    Console.WriteLine("ExecuteInternal: DockerCommandStop called");
                    break;
            }

            await context.Interaction.RespondAsync("Command has been sent. Awaiting response. This will take up to " + _dockerService.Settings.Retries * _dockerService.Settings.TimeBeforeRetry + " Seconds.");
            Console.WriteLine("ExecuteInternal: Command sent, awaiting response");

            for (int i = 0; i < _dockerService.Settings.Retries; i++)
            {
                Console.WriteLine($"ExecuteInternal: Retry {i} - Sleeping for {_dockerService.Settings.TimeBeforeRetry} seconds");
                Thread.Sleep(TimeSpan.FromSeconds(_dockerService.Settings.TimeBeforeRetry));
                await _dockerService.DockerUpdate();
                Console.WriteLine($"ExecuteInternal: DockerUpdate called in retry {i}");

                switch (command)
                {
                    case "start":
                        if (_dockerService.RunningDockers.Contains(dockerName))
                        {
                            await context.Interaction.FollowupAsync(context.User.Mention + " " + dockerName + " has been started");
                            Console.WriteLine("ExecuteInternal: Docker container started");
                            return;
                        }
                        break;
                    case "stop":
                        if (_dockerService.StoppedDockers.Contains(dockerName))
                        {
                            await context.Interaction.FollowupAsync(context.User.Mention + " " + dockerName + " has been stopped");
                            Console.WriteLine("ExecuteInternal: Docker container stopped");
                            return;
                        }
                        break;
                    case "restart":
                        if (_dockerService.RunningDockers.Contains(dockerName))
                        {
                            await context.Interaction.FollowupAsync(context.User.Mention + " " + dockerName + " has been restarted");
                            Console.WriteLine("ExecuteInternal: Docker container restarted");
                            return;
                        }
                        break;
                }
            }

            await _dockerService.DockerUpdate();
            Console.WriteLine("ExecuteInternal: Final DockerUpdate called");

            switch (command)
            {
                case "start":
                    if (_dockerService.RunningDockers.Contains(dockerName))
                    {
                        await context.Interaction.FollowupAsync(context.User.Mention + " " + dockerName + " has been started");
                        Console.WriteLine("ExecuteInternal: Docker container started after retries");
                        return;
                    }
                    await context.Interaction.FollowupAsync(context.User.Mention + " " + dockerName + " could not be started");
                    Console.WriteLine("ExecuteInternal: Docker container could not be started after retries");
                    break;
                case "stop":
                    if (_dockerService.StoppedDockers.Contains(dockerName))
                    {
                        await context.Interaction.FollowupAsync(context.User.Mention + " " + dockerName + " has been stopped");
                        Console.WriteLine("ExecuteInternal: Docker container stopped after retries");
                        return;
                    }
                    await context.Interaction.FollowupAsync(context.User.Mention + " " + dockerName + " could not be stopped");
                    Console.WriteLine("ExecuteInternal: Docker container could not be stopped after retries");
                    break;
                case "restart":
                    if (_dockerService.RunningDockers.Contains(dockerName))
                    {
                        await context.Interaction.FollowupAsync(context.User.Mention + " " + dockerName + " has been restarted");
                        Console.WriteLine("ExecuteInternal: Docker container restarted after retries");
                        return;
                    }
                    await context.Interaction.FollowupAsync(context.User.Mention + " " + dockerName + " could not be restarted");
                    Console.WriteLine("ExecuteInternal: Docker container could not be restarted after retries");
                    break;
            }
        }

        #endregion

        #region HelperMethods

        private static IEnumerable<string> GetPermissionsForRoles(DiscordSettings settings, IReadOnlyCollection<SocketRole> roles)
        {
            List<string> permissions = new List<string>();
            foreach (var role in roles)
            {
                if (settings.RoleStartPermissions.ContainsKey(role.Id))
                {
                    permissions.AddRange(settings.RoleStartPermissions[role.Id]);
                }
                if (settings.RoleStopPermissions.ContainsKey(role.Id))
                {
                    permissions.AddRange(settings.RoleStopPermissions[role.Id]);
                }
            }
            return permissions.Distinct();
        }

        private static IEnumerable<string> ValidateSectionLabels(DockerService dockerService, DiscordSettings settings, IReadOnlyCollection<SocketRole> roles)
        {
            List<string> containers = new List<string>();
            var dockerContainers = dockerService.DockerStatus;
            foreach (var container in dockerContainers)
            {
                if (container.Labels != null && container.Labels.ContainsKey("section"))
                {
                    var sectionLabel = container.Labels["section"];
                    if (settings.SectionOrder.Contains(sectionLabel))
                    {
                        foreach (var role in roles)
                        {
                            if (settings.RoleStartPermissions.ContainsKey(role.Id) && settings.RoleStartPermissions[role.Id].Contains(sectionLabel))
                            {
                                containers.Add(container.Names[0]);
                            }
                            if (settings.RoleStopPermissions.ContainsKey(role.Id) && settings.RoleStopPermissions[role.Id].Contains(sectionLabel))
                            {
                                containers.Add(container.Names[0]);
                            }
                        }
                    }
                }
            }
            return containers.Distinct();
        }

        #endregion
    }
}

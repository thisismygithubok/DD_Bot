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
using System.Threading;
using System.Threading.Tasks;
using DD_Bot.Application.Commands;
using DD_Bot.Application.Interfaces;
using DD_Bot.Domain;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DD_Bot.Application.Services
{
    public class DiscordService : IDiscordService
    {
        private readonly IConfigurationRoot _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly DiscordSocketClient _discordClient;
        private readonly DockerCommand _dockerCommand;

        public DiscordService(IConfigurationRoot configuration, IServiceProvider serviceProvider, ILogger<DockerCommand> logger)//Discord Initialising
        {
            var discordSocketConfig = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages
            };

            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _discordClient = new DiscordSocketClient(discordSocketConfig);
            _dockerCommand = new DockerCommand(_discordClient, Docker, Setting.DiscordSettings, logger); // Create an instance of DockerCommand
        }

        private Settings Setting => _configuration.Get<Settings>();

        private DockerService Docker => _serviceProvider.GetRequiredService<IDockerService>() as DockerService;
        private SettingsService SettingService => _serviceProvider.GetRequiredService<ISettingsService>() as SettingsService;

        public async Task Start()
        {
            // Subscribe to events
            _discordClient.Log += DiscordClient_Log;
            _discordClient.MessageReceived += DiscordClient_MessageReceived;
            _discordClient.SelectMenuExecuted += DiscordClient_SelectMenuExecuted;
            _discordClient.SlashCommandExecuted += DiscordClient_SlashCommandExecuted;
            _discordClient.Ready += DiscordClient_Ready; // Subscribe to the Ready event

            await _discordClient.LoginAsync(TokenType.Bot, Setting.DiscordSettings.Token);
            await _discordClient.StartAsync();
        }

        private async Task DiscordClient_Ready()
        {
            try
            {
                Console.WriteLine("DiscordClient is ready!");

                // Initialize commands after the client is ready
                await _dockerCommand.InitializeCommands();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ready Exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private async Task DiscordClient_SlashCommandExecuted(SocketSlashCommand arg)
        {
            try
            {
                switch (arg.CommandName)
                {
                    case "ping":
                        TestCommand.Execute(arg);
                        return;
                    case "docker":
                        await _dockerCommand.HandleSlashCommand(arg, Docker, Setting.DiscordSettings);
                        return;
                    case "list":
                        ListCommand.Execute(arg, Docker, Setting.DiscordSettings, Setting.DockerSettings);
                        return;
                    case "admin":
                        AdminCommand.Execute(arg, Setting, SettingService);
                        return;
                    case "user":
                        UserCommand.Execute(arg, Setting, SettingService);
                        return;
                    case "role":
                        RoleCommand.Execute(arg, Setting, SettingService);
                        return;
                    case "permission":
                        PermissionCommand.Execute(arg, Setting);
                        return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SlashCommandExecuted: {ex.Message}");
            }
        }

        private async Task DiscordClient_SelectMenuExecuted(SocketMessageComponent component)
        {
            try
            {
                if (component.Data.CustomId.StartsWith("section_select:"))
                {
                    await _dockerCommand.HandleSectionSelect(component, Docker, Setting.DiscordSettings);
                }
                else if (component.Data.CustomId.StartsWith("container_select:"))
                {
                    await _dockerCommand.HandleContainerSelect(component, Docker, Setting.DiscordSettings);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SelectMenuExecuted: Exception - {ex.Message}\n{ex.StackTrace}");
            }
        }

        private Task DiscordClient_MessageReceived(SocketMessage arg)
        {
            Console.WriteLine($"{arg.Author.Username}: {arg.Content}");
            return Task.CompletedTask;
        }

        private Task DiscordClient_Log(LogMessage arg)
        {
            Console.WriteLine($"{arg.Severity}:{arg.Message}");
            return Task.CompletedTask;
        }
    }
}

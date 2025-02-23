using System;
using System.Threading;
using System.Threading.Tasks;
using DD_Bot.Application.Commands;
using DD_Bot.Application.Interfaces;
using DD_Bot.Domain;
using Discord;
using Discord.WebSocket;
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

        public DiscordService(IConfigurationRoot configuration, IServiceProvider serviceProvider)//Discord Initialising
        {
            var discordSocketConfig = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages
            };

            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _discordClient = new DiscordSocketClient(discordSocketConfig);
            _dockerCommand = new DockerCommand(_discordClient, Docker); // Create an instance of DockerCommand
        }

        private Settings Setting => _configuration.Get<Settings>();

        private DockerService Docker => _serviceProvider.GetRequiredService<IDockerService>() as DockerService;
        private SettingsService SettingService => _serviceProvider.GetRequiredService<ISettingsService>() as SettingsService;

        public void Start() //Discord Start
        {
            _discordClient.Log += DiscordClient_Log;
            _discordClient.MessageReceived += DiscordClient_MessageReceived;
            _discordClient.GuildAvailable += DiscordClient_GuildAvailable;
            _discordClient.SlashCommandExecuted += DiscordClient_SlashCommandExecuted;
            _discordClient.SelectMenuExecuted += DiscordClient_SelectMenuExecuted;
            _discordClient.LoginAsync(TokenType.Bot, Setting.DiscordSettings.Token);
            _discordClient.StartAsync();
            while (true)
            {
                Thread.Sleep(1000);
            }
            // ReSharper disable once FunctionNeverReturns
        }

        private async Task DiscordClient_SlashCommandExecuted(SocketSlashCommand arg)
        {
            switch (arg.CommandName)
            {
                case "ping":
                    TestCommand.Execute(arg);
                    return;
                case "docker":
                    _dockerCommand.HandleSlashCommand(arg, Docker, Setting.DiscordSettings);
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

        private async Task DiscordClient_SelectMenuExecuted(SocketMessageComponent component)
        {
            switch (component.Data.CustomId)
            {
                case "section_select":
                    await _dockerCommand.HandleSectionSelect(component, Docker, Setting.DiscordSettings);
                    break;
                case "container_select":
                    await _dockerCommand.HandleContainerSelect(component, Docker, Setting.DiscordSettings);
                    break;
                case "command_select":
                    await _dockerCommand.HandleCommandSelect(component, Docker, Setting.DiscordSettings); // Add Docker and Setting.DiscordSettings
                    break;
            }
        }

        private async Task DiscordClient_GuildAvailable(SocketGuild guild)
        {
            var socketGuildUser = _discordClient.GetUser(guild.OwnerId) as SocketGuildUser;
            var userRoles = socketGuildUser.Roles;
            var sections = _dockerCommand.GetSectionsForRoles(Setting.DiscordSettings, userRoles);

            await Task.Run(() =>
            {
                guild.CreateApplicationCommandAsync(DockerCommand.Create(sections));
                guild.CreateApplicationCommandAsync(TestCommand.Create());
                guild.CreateApplicationCommandAsync(ListCommand.Create());
                guild.CreateApplicationCommandAsync(AdminCommand.Create());
                guild.CreateApplicationCommandAsync(UserCommand.Create());
                guild.CreateApplicationCommandAsync(RoleCommand.Create());
                guild.CreateApplicationCommandAsync(PermissionCommand.Create());
            });
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

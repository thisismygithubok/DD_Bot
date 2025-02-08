using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DD_Bot.Application.Providers;
using DD_Bot.Application.Interfaces;
using DD_Bot.Application.Services;
using DD_Bot.Domain;

string version = "1.0.1";
Console.WriteLine("DD_Bot, Version " + version);

#region CreateSettingsFiles
var settingsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "settings");
var settingsFile = Path.Combine(settingsDirectory, "settings.json");
if (!Directory.Exists(settingsDirectory))
{
    Directory.CreateDirectory(settingsDirectory);
}
if (!File.Exists(settingsFile))
{
    SettingsProvider.CreateBasicSettings(settingsFile);
}
#endregion

#region ReadSettingsFromFile
var configuration = new ConfigurationBuilder()
    .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "settings"))
    .AddJsonFile("settings.json", false, true)
    .Build();

File.WriteAllText(settingsFile, JsonConvert.SerializeObject(configuration.Get<Settings>(), Formatting.Indented));
#endregion

var services = new ServiceCollection()
    .AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
    {
        LogLevel = LogSeverity.Info,
        GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMessageReactions,
        MessageCacheSize = 100
    }))
    .AddSingleton<ILoggerFactory, LoggerFactory>()
    .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
    .AddLogging(configure => configure.AddConsole())
    .AddSingleton(sp =>
    {
        var client = sp.GetRequiredService<DiscordSocketClient>();
        var logger = sp.GetRequiredService<ILogger<DiscordUpdater>>();
        var guildId = ulong.Parse(Environment.GetEnvironmentVariable("GUILD_ID"));
        var channelId = ulong.Parse(Environment.GetEnvironmentVariable("CHANNEL_ID"));
        return new DiscordUpdater(client, guildId, channelId, logger);
    })
    .AddScoped(_ => configuration)
    .AddScoped(_ => settingsFile)
    .AddSingleton<IDiscordService, DiscordService>()
    .AddSingleton<IDockerService, DockerService>()
    .AddSingleton<ISettingsService, SettingsService>()
    .BuildServiceProvider();

var logger = services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting application...");

var discordUpdater = services.GetRequiredService<DiscordUpdater>();
await discordUpdater.StartAsync();

var dockerService = services.GetRequiredService<IDockerService>() as DockerService;
if (dockerService == null) throw new ArgumentNullException(nameof(dockerService));
var settingsService = services.GetRequiredService<ISettingsService>() as SettingsService;
if (settingsService == null) throw new ArgumentNullException(nameof(settingsService));
var discordBot = services.GetRequiredService<IDiscordService>() as DiscordService;
if (discordBot == null) throw new ArgumentNullException(nameof(discordBot));
discordBot.Start();
dockerService.Start();
settingsService.Start();

logger.LogInformation("Application started successfully.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Application exiting...");
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (TaskCanceledException)
{
    logger.LogInformation("Application terminated gracefully.");
}

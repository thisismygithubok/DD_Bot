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

var enableMetrics = configuration.GetValue<bool>("DiscordSettings:EnableMetrics");

var services = new ServiceCollection()
    .AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
    {
        LogLevel = LogSeverity.Warning, // Set log level to Warning to reduce additional info logs
        GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMessageReactions,
        MessageCacheSize = 100
    }))
    .AddSingleton<ILoggerFactory, LoggerFactory>()
    .AddSingleton(typeof(ILogger<>), typeof(Logger<>))
    .AddLogging(configure => configure
        .AddConsole(options =>
        {
            options.IncludeScopes = false;
            options.DisableColors = true;
            options.TimestampFormat = "hh:mm:ss ";
        })
    )
    .AddScoped(_ => configuration)
    .AddScoped(_ => settingsFile)
    .AddSingleton<IDiscordService, DiscordService>()
    .AddSingleton<IDockerService, DockerService>()
    .AddSingleton<ISettingsService, SettingsService>();

if (enableMetrics)
{
    services.AddSingleton(sp =>
    {
        var client = sp.GetRequiredService<DiscordSocketClient>();
        var logger = sp.GetRequiredService<ILogger<DiscordUpdater>>();
        var guildId = ulong.Parse(Environment.GetEnvironmentVariable("GUILD_ID"));
        var channelId = ulong.Parse(Environment.GetEnvironmentVariable("CHANNEL_ID"));
        return new DiscordUpdater(client, guildId, channelId, logger, enableMetrics);
    });
}

var serviceProvider = services.BuildServiceProvider();

var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
logger.LogDebug("Starting application...");

if (enableMetrics)
{
    var discordUpdater = serviceProvider.GetRequiredService<DiscordUpdater>();
    await discordUpdater.StartAsync();
}

var dockerService = serviceProvider.GetRequiredService<IDockerService>() as DockerService;
if (dockerService == null) throw new ArgumentNullException(nameof(dockerService));
var settingsService = serviceProvider.GetRequiredService<ISettingsService>() as SettingsService;
if (settingsService == null) throw new ArgumentNullException(nameof(settingsService));
var discordBot = serviceProvider.GetRequiredService<IDiscordService>() as DiscordService;
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

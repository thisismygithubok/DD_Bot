using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

public class DiscordUpdater
{
    private readonly DiscordSocketClient _client;
    private readonly ulong _guildId;
    private readonly ulong _channelId;
    private readonly ILogger<DiscordUpdater> _logger;
    private readonly Timer _timer;

    public DiscordUpdater(DiscordSocketClient client, ulong guildId, ulong channelId, ILogger<DiscordUpdater> logger)
    {
        _client = client;
        _guildId = guildId;
        _channelId = channelId;
        _logger = logger;
        _timer = new Timer(UpdateChannelName, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task StartAsync()
    {
        _client.Log += LogDiscord;
        _client.Ready += OnReadyAsync;

        try
        {
            _logger.LogInformation("Logging in to Discord...");
            await _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DISCORD_TOKEN"));
            _logger.LogInformation("Starting Discord client...");
            await _client.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error starting Discord client: {ex.Message}");
        }
    }

    private async Task OnReadyAsync()
    {
        _client.Ready -= OnReadyAsync; // Unsubscribe to ensure the logic is only triggered once
        _logger.LogInformation("Bot is ready.");

        await Task.Delay(1000); // Ensure there's a delay to allow for guilds to be fully fetched

        _logger.LogInformation($"Attempting to fetch guild with ID {_guildId}...");
        var guild = _client.GetGuild(_guildId);
        if (guild == null)
        {
            _logger.LogError($"Guild with ID {_guildId} not found. Here are the available guilds: {_client.Guilds.Count}");
            foreach (var g in _client.Guilds)
            {
                _logger.LogInformation($"Available guild: {g.Id} - {g.Name}");
            }
            return;
        }

        _logger.LogInformation($"Guild with ID {_guildId} found: {guild.Name}");

        var channel = guild.GetChannel(_channelId) as SocketTextChannel;
        if (channel == null)
        {
            _logger.LogError($"Channel with ID {_channelId} not found in guild {_guildId}.");
            return;
        }

        _logger.LogInformation($"Channel with ID {_channelId} found: {channel.Name}");
        
        // Start the timer to update channel name every 30 seconds
        _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private void UpdateChannelName(object state)
    {
        var cpuUsage = GetCpuUsage();
        var memoryUsage = GetMemoryUsage();

        var channel = _client.GetGuild(_guildId)?.GetChannel(_channelId) as SocketTextChannel;
        if (channel != null)
        {
            var newChannelName = $"CPU: {cpuUsage}% | RAM: {memoryUsage}%";
            channel.ModifyAsync(ch => ch.Name = newChannelName).GetAwaiter().GetResult();
            _logger.LogInformation($"Updated channel name to: {newChannelName}");
        }
    }

    private static double GetCpuUsage()
    {
        var startInfo = new ProcessStartInfo("sh", "-c \"top -bn1 | grep 'Cpu(s)' | awk '{print $2 + $4}'\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (var process = Process.Start(startInfo))
        {
            using (var reader = process.StandardOutput)
            {
                var result = reader.ReadToEnd().Trim();
                if (double.TryParse(result, out var cpuUsage))
                {
                    return cpuUsage;
                }
            }
        }
        return 0;
    }

    private static double GetMemoryUsage()
    {
        var startInfo = new ProcessStartInfo("sh", "-c \"free | grep Mem | awk '{print $3/$2 * 100.0}'\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (var process = Process.Start(startInfo))
        {
            using (var reader = process.StandardOutput)
            {
                var result = reader.ReadToEnd().Trim();
                if (double.TryParse(result, out var memoryUsage))
                {
                    return memoryUsage;
                }
            }
        }
        return 0;
    }

    private Task LogDiscord(LogMessage msg)
    {
        _logger.LogInformation(msg.ToString());
        return Task.CompletedTask;
    }
}

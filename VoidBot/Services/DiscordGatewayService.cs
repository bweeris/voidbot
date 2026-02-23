using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VoidBot.Services;

public class DiscordGatewayService : IHostedService, IAsyncDisposable
{
    private readonly ILogger<DiscordGatewayService> _logger;
    private readonly Options _options;

    private DiscordSocketClient? _client;
    private InteractionService? _interactionService;

    private readonly List<IMessage> _toDelete = new();
    private Timer _timer;

    public DiscordGatewayService(
        IOptions<Options> options,
        ILogger<DiscordGatewayService> logger
    )
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException(
                $"Missing configuration: `Token`."
            );
        }

        if (_options.GuildId == 0)
        {
            throw new InvalidOperationException(
                $"Missing configuration: `GuildId`."
            );
        }

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AutoModerationActionExecution | GatewayIntents.GuildMessages | GatewayIntents.Guilds,
        };

        _client = new DiscordSocketClient(config);
        _interactionService = new InteractionService(_client.Rest);
        _client.Log += OnDiscordLogAsync;
        _interactionService.Log += OnDiscordLogAsync;
        _client.MessageReceived += OnMessageReceivedAsync;
        _client.Ready += OnClientReadyAsync;

        _timer = new Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        await _client.LoginAsync(TokenType.Bot, _options.Token);
        await _client.StartAsync();
    }

    private async void OnTick(object? state)
    {
        if (_client is null)
        {
            return;
        }

        var toDelete = _toDelete.ToArray();
        _toDelete.Clear();

        var channel = await _client.GetChannelAsync(_options.ChannelId);
        if (channel is not ITextChannel textChannel)
        {
            return;
        }
        
        await textChannel.DeleteMessagesAsync(toDelete);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return;
        }

        _client.MessageReceived -= OnMessageReceivedAsync;
        _client.Ready -= OnClientReadyAsync;
        _client.Log -= OnDiscordLogAsync;
        if (_interactionService is not null)
        {
            _interactionService.Log -= OnDiscordLogAsync;
        }

        await _timer.DisposeAsync();

        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    private async Task OnClientReadyAsync()
    {
        _logger.LogInformation("Get channel {ChannelId}", _options.ChannelId);

        // Grab all messages in the target channel and delete them if they are older than the TTL, or create deletion tasks otherwise
        var channel = await _client.GetChannelAsync(_options.ChannelId);
        _logger.LogInformation("Connected to channel: {Channel}, type: {ChannelType}", channel.Id, channel.GetType().Name);

        if (channel is not ITextChannel textChannel)
        {
            return;
        }

        _logger.LogInformation("Get current messages");

        var startupTime = DateTimeOffset.UtcNow;
        var messages = textChannel.GetMessagesAsync();
        await foreach (var batch in messages.WithCancellation(CancellationToken.None))
        {
            foreach (var message in batch)
            {
                if (message.CreatedAt > startupTime)
                {
                    break;
                }
                if(_options.KeepMessages?.Contains(message.Id) ?? false) {
                    continue;
                }
                if (message.CreatedAt < startupTime - _options.TimeToLive)
                {
                    await message.DeleteAsync();
                }
                else
                {
                    // We spin up a new task for each message to be deleted after the period
                    _ = Task.Run(async () =>
                    {
                        var delay = (message.CreatedAt + _options.TimeToLive) - DateTimeOffset.UtcNow;
                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay);
                        }
                        _toDelete.Add(message);
                    });
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    private Task OnDiscordLogAsync(LogMessage message)
    {
        var logLevel = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information,
        };

        _logger.Log(
            logLevel,
            message.Exception,
            "{source}: {message}",
            message.Source,
            message.Message
        );

        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(SocketMessage socketMessage)
    {
        if (_client is null)
        {
            return;
        }

        if (socketMessage.Channel is not ITextChannel guildChannel)
        {
            return;
        }

        if (guildChannel.Guild.Id != _options.GuildId || guildChannel.Id != _options.ChannelId)
        {
            return;
        }

        // We spin up a new task for each message to be deleted after the period
        _ = Task.Run(async () =>
        {
            await Task.Delay(_options.TimeToLive);
            _logger.LogInformation("Deleting message: {MessageIdl}", socketMessage.Id);
            _toDelete.Add(socketMessage);
        });
    }

}

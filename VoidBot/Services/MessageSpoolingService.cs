using Discord;
using Microsoft.Extensions.Hosting;

namespace VoidBot.Services;

public class MessageSpoolingService : BackgroundService
{
    private readonly DiscordGatewayService _client;
    
    private readonly PeriodicTimer _timer;
    public static List<IMessage> ToDelete = new();

    public MessageSpoolingService(DiscordGatewayService client)
    {
        _client = client;
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _timer.WaitForNextTickAsync(stoppingToken);
            var toDelete = Interlocked.Exchange(ref ToDelete, new List<IMessage>());
            await _client.DeleteMessages(toDelete);
        }
    }
}

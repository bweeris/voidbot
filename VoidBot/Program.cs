// See https://aka.ms/new-console-template for more information

using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VoidBot.Services;

await Parser.Default.ParseArguments<Options>(args)
    .WithNotParsed(errors =>
    {
        Console.WriteLine("Failed to parse command line arguments:");
        foreach (var error in errors)
        {
            Console.WriteLine(error.ToString());
        }

        Environment.Exit(1);
    })
    .WithParsedAsync(async options =>
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.Configure<Options>(o =>
        {
            o.ChannelId = options.ChannelId;
            o.Token = options.Token;
            o.Ttl = options.Ttl;
        });
        
        builder.Services.AddHostedService<DiscordGatewayService>();

        var host = builder.Build();
        await host.RunAsync();
    });

public class Options
{
    [Option('t', "token", Required = true, HelpText = "The token to use for authentication.")]
    public string Token { get; set; }

    [Option('g', "guild", Required = true, HelpText = "The guild ID to connect to.")]
    public uint GuildId { get; set; }

    [Option('c', "channel", Required = true, HelpText = "The channel ID to connect to.")]
    public uint ChannelId { get; set; }

    [Option('t', "ttl", Required = false, HelpText = "The time to live before messages in the channel are deleted.")]
    public string Ttl { get; set; } = "1m";

    public TimeSpan TimeToLive => Ttl switch
    {
        { } s when s.EndsWith("s") && int.TryParse(s[..^1], out var seconds) => TimeSpan.FromSeconds(seconds),
        { } s when s.EndsWith("m") && int.TryParse(s[..^1], out var minutes) => TimeSpan.FromMinutes(minutes),
        { } s when s.EndsWith("h") && int.TryParse(s[..^1], out var hours) => TimeSpan.FromHours(hours),
        { } s when s.EndsWith("D") && int.TryParse(s[..^1], out var days) => TimeSpan.FromDays(days),
        _ => throw new ArgumentException("Invalid TTL format. Use 's' for seconds, 'm' for minutes, or 'h' for hours.")
    };
}
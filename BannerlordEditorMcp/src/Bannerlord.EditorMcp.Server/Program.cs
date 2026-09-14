using Bannerlord.EditorMcp.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(BridgeServerOptions.FromEnvironment());
builder.Services.AddSingleton<BridgeConnectionManager>();
builder.Services.AddSingleton<IBridgeClient>(services => services.GetRequiredService<BridgeConnectionManager>());
builder.Services.AddHostedService(services => services.GetRequiredService<BridgeConnectionManager>());

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Reign.Mcp.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var options = ReignMcpOptions.FromEnvironment();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<SensitiveDataRedactor>();
builder.Services.AddSingleton<WorkspaceAccess>();
builder.Services.AddSingleton<TestingCatalogService>();
builder.Services.AddSingleton<CampaignTestService>();
builder.Services.AddSingleton<ReignProcessRunner>();
builder.Services.AddSingleton<ReignProjectCatalog>();
builder.Services.AddSingleton<IReignRepositoryHygieneAudit, ReignRepositoryHygieneAudit>();
builder.Services.AddSingleton<ReignValidationService>();
builder.Services.AddSingleton<ReignBuildService>();
builder.Services.AddSingleton(_ =>
{
    var handler = new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    };
    return new HttpClient(handler)
    {
        BaseAddress = options.ServerBaseUri,
        Timeout = options.ApiTimeout
    };
});
builder.Services.AddSingleton<ReignApiClient>();

builder.Services
    .AddMcpServer(server =>
    {
        server.ServerInfo = new()
        {
            Name = "reign",
            Version = "0.2.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

if (args.Contains("--plan-cli", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--validate-cli", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--validation-status-cli", StringComparer.OrdinalIgnoreCase)
    || args.Contains("--linux-server-cli", StringComparer.OrdinalIgnoreCase))
{
    using var validationHost = builder.Build();
    var validation = validationHost.Services.GetRequiredService<ReignValidationService>();
    if (args.Contains("--linux-server-cli", StringComparer.OrdinalIgnoreCase))
    {
        var linux = await validation.BuildLinuxServerAsync(args.Contains("--restore"),
            CliValue(args, "--task-id", Environment.GetEnvironmentVariable("CODEX_THREAD_ID") ?? ""));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(linux,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = linux.Ok ? 0 : 1;
        return;
    }

    if (args.Contains("--validation-status-cli", StringComparer.OrdinalIgnoreCase))
    {
        var status = validation.GetStatus();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(status,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = status.Status == "unavailable" ? 1 : 0;
        return;
    }
    var profile = CliValue(args, "--profile", "all");
    var configuration = CliValue(args, "--configuration", "Release");
    var changedPaths = CliValue(args, "--changed-paths", "");
    var restore = args.Contains("--restore", StringComparer.OrdinalIgnoreCase);
    var failFast = !args.Contains("--no-fail-fast", StringComparer.OrdinalIgnoreCase);
    if (args.Contains("--plan-cli", StringComparer.OrdinalIgnoreCase))
    {
        var plan = validation.GetPlan(profile, configuration, restore, changedPaths);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
            plan,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = plan.Status == "ready" && plan.CoverageComplete ? 0 : 1;
        return;
    }
    var report = await validation.ValidateAsync(
        profile,
        configuration,
        restore,
        changedPaths,
        failFast,
        CancellationToken.None,
        CliValue(args, "--task-id", Environment.GetEnvironmentVariable("CODEX_THREAD_ID") ?? ""));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        report,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Environment.ExitCode = report.Ok ? 0 : 1;
    return;
}

await builder.Build().RunAsync();

static string CliValue(string[] arguments, string name, string fallback)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return arguments[index + 1];
        }
    }
    return fallback;
}

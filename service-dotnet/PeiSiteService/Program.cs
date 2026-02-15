using System.Text.Json;
using System.Text.Json.Serialization;
using PeiSiteService;
using PeiSiteService.Services;

// Unhandled exception handlers
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.WriteLine($"[WARN] Unobserved task exception: {e.Exception}");
    e.SetObserved();
};

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization globally (camelCase to match Node.js API)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// Configure Kestrel to listen only on localhost:47836
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(47836);
});

// Enable Windows Service lifetime (SCM integration)
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PEI Site Service";
});

// Register services
var logger = new FileLogger();
var configManager = new ConfigManager(logger);
var config = configManager.GetConfig();
var ftpConfig = configManager.GetFtpConfig();

logger.Log($"[PEI Site Service] Configuration loaded: apiUrl={config.ApiUrl}, siteId={config.SiteId}, tenantId={config.TenantId}");
logger.Log($"[PEI Site Service] FTP config: enabled={ftpConfig.FtpEnabled}, host={ftpConfig.FtpHost}");

var wsClient = new WebSocketClient(config.ToServiceConfig(), logger);
var ftpWatcher = new FtpWatcher(ftpConfig, wsClient, config.SiteId, config.TenantId, logger);

builder.Services.AddSingleton(logger);
builder.Services.AddSingleton(configManager);
builder.Services.AddSingleton(wsClient);
builder.Services.AddSingleton(ftpWatcher);
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// Map API endpoints
ApiServer.MapEndpoints(app);

app.Run();

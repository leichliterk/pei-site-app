using System.Text.Json;
using System.Text.Json.Serialization;
using PeiSiteService;
using PeiSiteService.Plc;
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

logger.MinLevel = config.LogLevel;

logger.Log($"[PEI Site Service] Configuration loaded: apiUrl={config.ApiUrl}, siteId={config.SiteId}, tenantId={config.TenantId}, logLevel={config.LogLevel}");
logger.Log($"[PEI Site Service] FTP config: enabled={ftpConfig.FtpEnabled}, servers={ftpConfig.Servers.Count}");

var fileQueue = new FileQueue(logger);
var ftpManager = new FtpWatcherManager(fileQueue, logger, configManager);
ftpManager.Initialize(ftpConfig, config.SiteId, config.TenantId);
var wsClient = new WebSocketClient(config.ToServiceConfig(), logger, ftpManager, fileQueue);
var fileBroker = new FileBroker(fileQueue, wsClient, logger);
var notificationManager = new NotificationManager();
var tagBrowserState = new TagBrowserState();
var plcSnapshotState = new PlcSnapshotState();

builder.Services.AddSingleton(logger);
builder.Services.AddSingleton(configManager);
builder.Services.AddSingleton(wsClient);
builder.Services.AddSingleton(fileQueue);
builder.Services.AddSingleton(fileBroker);
builder.Services.AddSingleton(ftpManager);
builder.Services.AddSingleton(notificationManager);
builder.Services.AddSingleton(tagBrowserState);
builder.Services.AddSingleton(plcSnapshotState);
builder.Services.AddSingleton<PlcTagReaderFactory>();
builder.Services.AddSingleton<PlcPollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PlcPollingService>());
builder.Services.AddSingleton<TagBrowserService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TagBrowserService>());
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// Wire up PLC services to WebSocket client
wsClient.AttachPlcServices(
    app.Services.GetRequiredService<PlcPollingService>(),
    tagBrowserState,
    plcSnapshotState);

// Map API endpoints
ApiServer.MapEndpoints(app);

app.Run();

using PeiSiteService.Models;

namespace PeiSiteService.Services;

public static class ApiServer
{
    public static void MapEndpoints(WebApplication app)
    {
        // CORS middleware
        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, OPTIONS");
            context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type");

            if (context.Request.Method == "OPTIONS")
            {
                context.Response.StatusCode = 204;
                return;
            }
            await next();
        });

        // GET /health
        app.MapGet("/health", () => new
        {
            status = "ok",
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        });

        // GET /status
        app.MapGet("/status", (WebSocketClient ws) => new
        {
            status = ws.Status.ToString(),
            connectedAt = ws.ConnectedAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            currentUptime = ws.CurrentUptime,
            connectionHistory = ws.ConnectionHistory
        });

        // GET /history
        app.MapGet("/history", (WebSocketClient ws) => new
        {
            history = ws.ConnectionHistory,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        });

        // POST /config
        app.MapPost("/config", (ConfigUpdateRequest req, WebSocketClient ws, ConfigManager cfg) =>
        {
            cfg.UpdateConfig(req);
            ws.UpdateConfig(req);
            return new { success = true };
        });

        // POST /reconnect
        app.MapPost("/reconnect", (WebSocketClient ws) =>
        {
            ws.Disconnect();
            ws.Connect();
            return new { success = true };
        });

        // POST /prepopulate-history
        app.MapPost("/prepopulate-history", (PrepopulateRequest req, WebSocketClient ws) =>
        {
            if (req.Sessions == null)
                return Results.BadRequest(new { error = "sessions array required" });
            ws.PrepopulateHistory(req.Sessions);
            return Results.Ok(new { success = true });
        });

        // GET /ftp/status
        app.MapGet("/ftp/status", (FtpWatcher ftp) => ftp.GetStatus());

        // POST /ftp/config
        app.MapPost("/ftp/config", (FtpConfigUpdate req, FtpWatcher ftp, ConfigManager cfg) =>
        {
            cfg.UpdateConfig(req);
            ftp.UpdateConfig(cfg.GetFtpConfig());
            return new { success = true };
        });

        // POST /ftp/test
        app.MapPost("/ftp/test", async (FtpTestRequest req, FtpWatcher ftp) =>
        {
            if (string.IsNullOrEmpty(req.Host))
                return Results.BadRequest(new { success = false, message = "host is required" });
            var result = await ftp.TestConnectionAsync(req.Host, req.Path ?? "/");
            return Results.Ok(result);
        });

        // POST /ftp/poll
        app.MapPost("/ftp/poll", async (FtpWatcher ftp) =>
        {
            await ftp.PollAsync();
            return ftp.GetStatus();
        });
    }
}

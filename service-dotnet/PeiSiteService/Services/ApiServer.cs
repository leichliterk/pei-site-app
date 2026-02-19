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
            context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
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

        // --- FTP endpoints ---

        // GET /ftp/status — overall status (enabled + all server statuses)
        app.MapGet("/ftp/status", (FtpWatcherManager mgr) => mgr.GetOverallStatus());

        // PUT /ftp/enabled — toggle global FTP enabled
        app.MapPut("/ftp/enabled", (FtpEnabledRequest req, FtpWatcherManager mgr, ConfigManager cfg) =>
        {
            cfg.SetFtpEnabled(req.Enabled);
            mgr.SetEnabled(req.Enabled);
            return new { success = true };
        });

        // POST /ftp/servers — create a new FTP server
        app.MapPost("/ftp/servers", (FtpServerCreateRequest req, FtpWatcherManager mgr, ConfigManager cfg) =>
        {
            var server = cfg.AddFtpServer(req);
            mgr.AddServer(server);
            return Results.Ok(server);
        });

        // PUT /ftp/servers/{id} — update an existing FTP server
        app.MapPut("/ftp/servers/{id}", (string id, FtpServerUpdateRequest req, FtpWatcherManager mgr, ConfigManager cfg) =>
        {
            var updated = cfg.UpdateFtpServer(id, req);
            if (updated == null)
                return Results.NotFound(new { error = "Server not found" });
            mgr.UpdateServer(updated);
            return Results.Ok(updated);
        });

        // DELETE /ftp/servers/{id} — remove an FTP server
        app.MapDelete("/ftp/servers/{id}", (string id, FtpWatcherManager mgr, ConfigManager cfg) =>
        {
            if (!cfg.RemoveFtpServer(id))
                return Results.NotFound(new { error = "Server not found" });
            mgr.RemoveServer(id);
            return Results.Ok(new { success = true });
        });

        // POST /ftp/test — test FTP connection
        app.MapPost("/ftp/test", async (FtpTestRequest req, FtpWatcherManager mgr) =>
        {
            if (string.IsNullOrEmpty(req.Host))
                return Results.BadRequest(new { success = false, message = "host is required" });
            var result = await mgr.TestConnectionAsync(req.Host, req.Path ?? "/");
            return Results.Ok(result);
        });

        // POST /ftp/browse — browse FTP directories
        app.MapPost("/ftp/browse", async (FtpBrowseRequest req, FtpWatcherManager mgr) =>
        {
            if (string.IsNullOrEmpty(req.Host))
                return Results.BadRequest(new { success = false, message = "host is required" });
            var result = await mgr.BrowseDirectoryAsync(req.Host, req.Path);
            return Results.Ok(result);
        });
    }
}

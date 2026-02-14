using System.Text;
using System.Text.Json;
using FluentFTP;
using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class FtpWatcher
{
    private Models.FtpConfig _config;
    private readonly WebSocketClient _wsClient;
    private readonly int _siteId;
    private readonly int _tenantId;
    private readonly FileLogger _logger;
    private readonly string _statePath;

    private Timer? _pollTimer;
    private FtpState _state = new();
    private string _lastResult = "never polled";
    private int _filesForwarded;
    private int _isPolling; // 0 = not polling, 1 = polling (used with Interlocked)
    private readonly object _stateLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FtpWatcher(Models.FtpConfig config, WebSocketClient wsClient, int siteId, int tenantId, FileLogger logger)
    {
        _config = config;
        _wsClient = wsClient;
        _siteId = siteId;
        _tenantId = tenantId;
        _logger = logger;

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var stateDir = Path.Combine(programData, "PEI Site Service");
        try { Directory.CreateDirectory(stateDir); } catch { }
        _statePath = Path.Combine(stateDir, "ftp-state.json");
        LoadState();
    }

    public FtpWatcherStatus GetStatus()
    {
        lock (_stateLock)
        {
            return new FtpWatcherStatus
            {
                Enabled = _config.FtpEnabled,
                Host = _config.FtpHost,
                Path = _config.FtpPath,
                PollInterval = _config.FtpPollInterval,
                LastPoll = _state.LastPoll,
                LastResult = _lastResult,
                FilesForwarded = _filesForwarded,
                IsPolling = _isPolling == 1
            };
        }
    }

    public void UpdateConfig(Models.FtpConfig config)
    {
        var wasEnabled = _config.FtpEnabled;
        _config = config;

        if (config.FtpEnabled && !wasEnabled) Start();
        else if (!config.FtpEnabled && wasEnabled) Stop();
        else if (config.FtpEnabled) { Stop(); Start(); }
    }

    public void Start()
    {
        if (!_config.FtpEnabled || string.IsNullOrEmpty(_config.FtpHost))
        {
            _logger.Log("[FtpWatcher] Not starting - FTP is disabled or no host configured");
            return;
        }

        if (_pollTimer != null)
        {
            _logger.Log("[FtpWatcher] Already running");
            return;
        }

        _logger.Log($"[FtpWatcher] Starting - host: {_config.FtpHost}, path: {_config.FtpPath}, interval: {_config.FtpPollInterval}s");

        // Poll immediately, then on interval
        _ = PollAsync();
        _pollTimer = new Timer(_ => _ = PollAsync(), null,
            TimeSpan.FromSeconds(_config.FtpPollInterval),
            TimeSpan.FromSeconds(_config.FtpPollInterval));
    }

    public void Stop()
    {
        if (_pollTimer != null)
        {
            _pollTimer.Dispose();
            _pollTimer = null;
            _logger.Log("[FtpWatcher] Stopped");
        }
    }

    public async Task PollAsync()
    {
        if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0)
        {
            _logger.Log("[FtpWatcher] Poll already in progress, skipping");
            return;
        }

        var client = new AsyncFtpClient(_config.FtpHost, "anonymous", "anonymous@");
        client.Config.EncryptionMode = FtpEncryptionMode.None;

        try
        {
            _logger.Log($"[FtpWatcher] Connecting to {_config.FtpHost}...");
            await client.Connect();

            _logger.Log($"[FtpWatcher] Listing {_config.FtpPath}");
            var listing = await client.GetListing(_config.FtpPath);
            var files = listing.Where(f => f.Type == FtpObjectType.File).ToList();
            int newOrChanged = 0;

            foreach (var file in files)
            {
                var modifiedAt = file.Modified.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                FtpFileState? existing;
                lock (_stateLock) { _state.Files.TryGetValue(file.Name, out existing); }

                var isNew = existing == null;
                var isChanged = existing != null &&
                    (existing.Size != file.Size || existing.ModifiedAt != modifiedAt);

                if (isNew || isChanged)
                {
                    _logger.Log($"[FtpWatcher] {(isNew ? "New" : "Changed")} file: {file.Name} ({file.Size} bytes)");

                    try
                    {
                        var remotePath = _config.FtpPath.EndsWith("/")
                            ? $"{_config.FtpPath}{file.Name}"
                            : $"{_config.FtpPath}/{file.Name}";

                        var bytes = await client.DownloadBytes(remotePath, CancellationToken.None);
                        if (bytes != null)
                        {
                            var content = Encoding.UTF8.GetString(bytes);
                            ForwardFile(file.Name, content);
                            newOrChanged++;

                            lock (_stateLock)
                            {
                                _state.Files[file.Name] = new FtpFileState
                                {
                                    Name = file.Name,
                                    Size = file.Size,
                                    ModifiedAt = modifiedAt
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"[FtpWatcher] Error downloading {file.Name}: {ex.Message}");
                    }
                }
            }

            lock (_stateLock) { _state.LastPoll = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"); }
            SaveState();

            _lastResult = $"OK - {files.Count} files listed, {newOrChanged} forwarded";
            _logger.Log($"[FtpWatcher] Poll complete: {_lastResult}");
        }
        catch (Exception ex)
        {
            _lastResult = $"Error: {ex.Message}";
            _logger.Log($"[FtpWatcher] Poll failed: {_lastResult}");
        }
        finally
        {
            await client.Disconnect();
            client.Dispose();
            Interlocked.Exchange(ref _isPolling, 0);
        }
    }

    public async Task<FtpTestResult> TestConnectionAsync(string host, string remotePath)
    {
        var client = new AsyncFtpClient(host, "anonymous", "anonymous@");
        client.Config.EncryptionMode = FtpEncryptionMode.None;

        try
        {
            _logger.Log($"[FtpWatcher] Testing connection to {host}{remotePath}...");
            await client.Connect();
            await client.SetWorkingDirectory(remotePath);
            var list = await client.GetListing();

            _logger.Log($"[FtpWatcher] Test connection successful: {list.Length} items found");
            return new FtpTestResult
            {
                Success = true,
                Message = $"Connected successfully. Found {list.Length} items in {remotePath}",
                FileCount = list.Length
            };
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            _logger.Log($"[FtpWatcher] Test connection failed: {message}");
            return new FtpTestResult { Success = false, Message = message };
        }
        finally
        {
            await client.Disconnect();
            client.Dispose();
        }
    }

    private void ForwardFile(string filename, string content)
    {
        var payload = new
        {
            filename,
            content,
            siteId = _siteId,
            tenantId = _tenantId,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        _wsClient.EmitToServer("ftp:file", payload);
        Interlocked.Increment(ref _filesForwarded);
        _logger.Log($"[FtpWatcher] Forwarded: {filename} ({content.Length} chars)");
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var data = File.ReadAllText(_statePath);
                var state = JsonSerializer.Deserialize<FtpState>(data, JsonOptions);
                if (state != null)
                {
                    _state = state;
                    _logger.Log($"[FtpWatcher] Loaded state: {_state.Files.Count} tracked files");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"[FtpWatcher] Could not load state: {ex.Message}");
            _state = new FtpState();
        }
    }

    private void SaveState()
    {
        try
        {
            string json;
            lock (_stateLock) { json = JsonSerializer.Serialize(_state, JsonOptions); }
            File.WriteAllText(_statePath, json);
        }
        catch (Exception ex)
        {
            _logger.Log($"[FtpWatcher] Could not save state: {ex.Message}");
        }
    }
}

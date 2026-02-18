using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class FtpWatcher
{
    private FtpServerConfig _serverConfig;
    private readonly WebSocketClient _wsClient;
    private readonly int _siteId;
    private readonly int _tenantId;
    private readonly FileLogger _logger;
    private readonly string _statePath;

    private Timer? _pollTimer;
    private FtpState _state = new();
    private string _lastResult = "never polled";
    private int _filesForwarded;
    private int _isPolling;
    private readonly object _stateLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Id => _serverConfig.Id;
    public string Host => _serverConfig.FtpHost;
    public bool IsCurrentlyPolling => _isPolling == 1;

    public FtpWatcher(FtpServerConfig serverConfig, WebSocketClient wsClient, int siteId, int tenantId, FileLogger logger)
    {
        _serverConfig = serverConfig;
        _wsClient = wsClient;
        _siteId = siteId;
        _tenantId = tenantId;
        _logger = logger;

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var stateDir = Path.Combine(programData, "PEI Site Service");
        try { Directory.CreateDirectory(stateDir); } catch { }
        _statePath = Path.Combine(stateDir, $"ftp-state-{serverConfig.Id}.json");
        LoadState();
    }

    public FtpWatcherStatus GetStatus()
    {
        lock (_stateLock)
        {
            return new FtpWatcherStatus
            {
                Id = _serverConfig.Id,
                Host = _serverConfig.FtpHost,
                Path = _serverConfig.FtpPath,
                PollInterval = _serverConfig.FtpPollInterval,
                LastPoll = _state.LastPoll,
                LastResult = _lastResult,
                FilesForwarded = _filesForwarded,
                IsPolling = _isPolling == 1
            };
        }
    }

    public void UpdateConfig(FtpServerConfig config)
    {
        _serverConfig = config;
    }

    public void Start()
    {
        if (string.IsNullOrEmpty(_serverConfig.FtpHost))
        {
            _logger.Log($"[FtpWatcher:{Id}] Not starting - no host configured");
            return;
        }

        if (_pollTimer != null)
        {
            _logger.Log($"[FtpWatcher:{Id}] Already running");
            return;
        }

        _logger.Log($"[FtpWatcher:{Id}] Starting - host: {_serverConfig.FtpHost}, path: {_serverConfig.FtpPath}, interval: {_serverConfig.FtpPollInterval}s");

        _ = PollAsync();
        _pollTimer = new Timer(_ => _ = PollAsync(), null,
            TimeSpan.FromSeconds(_serverConfig.FtpPollInterval),
            TimeSpan.FromSeconds(_serverConfig.FtpPollInterval));
    }

    public void Stop()
    {
        if (_pollTimer != null)
        {
            _pollTimer.Dispose();
            _pollTimer = null;
            _logger.Log($"[FtpWatcher:{Id}] Stopped");
        }
    }

    // --- Raw TCP FTP helpers ---

    private IPAddress? GetLocalAddressFor(string ftpHost)
    {
        try
        {
            var hostAddresses = Dns.GetHostAddresses(ftpHost);
            var targetIp = hostAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (targetIp == null) return null;

            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(targetIp, 21);
            var localIp = (socket.LocalEndPoint as IPEndPoint)?.Address;
            return localIp;
        }
        catch { return null; }
    }

    private async Task<string?> SendFtpCommandAsync(StreamWriter writer, StreamReader reader, string command, string label)
    {
        await writer.WriteLineAsync(command);
        var resp = await reader.ReadLineAsync();
        _logger.Log($"[RawFTP:{Id}] {label}: {resp}");
        return resp;
    }

    private async Task<string?> FtpActiveDataTransferAsync(
        StreamWriter writer, StreamReader reader,
        IPAddress localIp, string command, string label)
    {
        var bytes = await FtpActiveDataTransferBytesAsync(writer, reader, localIp, command, label);
        if (bytes == null) return null;
        return Encoding.ASCII.GetString(bytes);
    }

    private async Task<byte[]?> FtpActiveDataTransferBytesAsync(
        StreamWriter writer, StreamReader reader,
        IPAddress localIp, string command, string label)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(localIp, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var ipBytes = localIp.GetAddressBytes();
            var portCmd = $"PORT {ipBytes[0]},{ipBytes[1]},{ipBytes[2]},{ipBytes[3]},{port / 256},{port % 256}";
            var resp = await SendFtpCommandAsync(writer, reader, portCmd, "PORT");
            if (resp == null || !resp.StartsWith("200")) return null;

            resp = await SendFtpCommandAsync(writer, reader, command, label);
            if (resp == null || !resp.StartsWith("150")) return null;

            var acceptTask = listener.AcceptTcpClientAsync();
            var completed = await Task.WhenAny(acceptTask, Task.Delay(30000));
            if (completed != acceptTask)
            {
                _logger.Log($"[RawFTP:{Id}] Timeout waiting for data connection ({label})");
                return null;
            }

            using var dataClient = acceptTask.Result;
            var dataStream = dataClient.GetStream();
            dataStream.ReadTimeout = 30000;

            using var ms = new MemoryStream();
            await dataStream.CopyToAsync(ms);

            resp = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP:{Id}] {label} transfer: {resp}");

            return ms.ToArray();
        }
        finally
        {
            listener?.Stop();
        }
    }

    /// <summary>
    /// Connects to the FTP host and returns a control channel (TcpClient, StreamReader, StreamWriter).
    /// Handles banner, login, and CWD. Returns null if any step fails.
    /// </summary>
    private async Task<(TcpClient control, StreamReader reader, StreamWriter writer)?> ConnectAndLoginAsync(string host, string path)
    {
        var control = new TcpClient();
        try
        {
            await control.ConnectAsync(host, 21);
            var stream = control.GetStream();
            stream.ReadTimeout = 15000;
            stream.WriteTimeout = 15000;
            var reader = new StreamReader(stream, Encoding.ASCII);
            var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            await reader.ReadLineAsync(); // banner

            await SendFtpCommandAsync(writer, reader, "USER anonymous", "USER");
            var resp = await SendFtpCommandAsync(writer, reader, "PASS anonymous@", "PASS");
            if (resp == null || !resp.StartsWith("230"))
            {
                control.Dispose();
                return null;
            }

            // Only CWD if path is not root
            var cwdPath = path.TrimEnd('/');
            if (!string.IsNullOrEmpty(cwdPath))
            {
                resp = await SendFtpCommandAsync(writer, reader, $"CWD {cwdPath}", "CWD");
                if (resp == null || !resp.StartsWith("250"))
                {
                    control.Dispose();
                    return null;
                }
            }

            return (control, reader, writer);
        }
        catch
        {
            control.Dispose();
            throw;
        }
    }

    // --- Public operations ---

    public async Task PollAsync()
    {
        if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0)
        {
            _logger.Log($"[FtpWatcher:{Id}] Poll already in progress, skipping");
            return;
        }

        TcpClient? control = null;
        try
        {
            _logger.Log($"[FtpWatcher:{Id}] Connecting to {_serverConfig.FtpHost}...");
            var conn = await ConnectAndLoginAsync(_serverConfig.FtpHost, _serverConfig.FtpPath);
            if (conn == null)
            {
                _lastResult = "Error: Login or CWD failed";
                return;
            }

            control = conn.Value.control;
            var reader = conn.Value.reader;
            var writer = conn.Value.writer;

            var localIp = GetLocalAddressFor(_serverConfig.FtpHost) ?? IPAddress.Any;

            // NLST uses ASCII mode (text listing)
            var nlstData = await FtpActiveDataTransferAsync(writer, reader, localIp, "NLST", "NLST");
            if (nlstData == null)
            {
                _lastResult = "Error: Could not retrieve file listing";
                return;
            }

            var fileNames = nlstData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _logger.Log($"[FtpWatcher:{Id}] NLST returned {fileNames.Length} files");
            int newOrChanged = 0;

            // Switch to binary mode for file transfers (forensic byte-for-byte fidelity)
            var typeResp = await SendFtpCommandAsync(writer, reader, "TYPE I", "TYPE I");
            if (typeResp == null || !typeResp.StartsWith("200"))
            {
                _lastResult = "Error: Could not switch to binary transfer mode";
                return;
            }

            foreach (var fileName in fileNames)
            {
                FtpFileState? existing;
                lock (_stateLock) { _state.Files.TryGetValue(fileName, out existing); }
                if (existing != null) continue;

                _logger.Log($"[FtpWatcher:{Id}] New file: {fileName}");
                try
                {
                    var fileBytes = await FtpActiveDataTransferBytesAsync(writer, reader, localIp, $"RETR {fileName}", $"RETR {fileName}");
                    if (fileBytes != null)
                    {
                        ForwardFile(fileName, fileBytes);
                        newOrChanged++;
                        lock (_stateLock)
                        {
                            _state.Files[fileName] = new FtpFileState
                            {
                                Name = fileName,
                                Size = fileBytes.Length,
                                ModifiedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"[FtpWatcher:{Id}] Error downloading {fileName}: {ex.Message}");
                }
            }

            lock (_stateLock) { _state.LastPoll = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"); }
            SaveState();

            _lastResult = $"OK - {fileNames.Length} files listed, {newOrChanged} forwarded";
            _logger.Log($"[FtpWatcher:{Id}] Poll complete: {_lastResult}");

            await writer.WriteLineAsync("QUIT");
        }
        catch (Exception ex)
        {
            _lastResult = $"Error: {ex.Message}";
            _logger.Log($"[FtpWatcher:{Id}] Poll failed: {_lastResult}");
        }
        finally
        {
            control?.Dispose();
            Interlocked.Exchange(ref _isPolling, 0);
        }
    }

    public async Task<FtpTestResult> TestConnectionAsync(string host, string remotePath)
    {
        _logger.Log($"[FtpWatcher] Testing connection (raw TCP) to {host}{remotePath}...");

        TcpClient? control = null;
        try
        {
            var conn = await ConnectAndLoginAsync(host, remotePath);
            if (conn == null)
                return new FtpTestResult { Success = false, Message = "Login or CWD failed" };

            control = conn.Value.control;
            var reader = conn.Value.reader;
            var writer = conn.Value.writer;

            var localIp = GetLocalAddressFor(host) ?? IPAddress.Any;
            var nlstData = await FtpActiveDataTransferAsync(writer, reader, localIp, "NLST", "NLST");
            if (nlstData == null)
                return new FtpTestResult { Success = false, Message = "Could not retrieve file listing (data connection failed)" };

            var fileNames = nlstData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _logger.Log($"[RawFTP] Parsed {fileNames.Length} file names");

            await writer.WriteLineAsync("QUIT");

            return new FtpTestResult
            {
                Success = true,
                Message = $"Connected successfully. Found {fileNames.Length} items in {remotePath}",
                FileCount = fileNames.Length
            };
        }
        catch (Exception ex)
        {
            _logger.Log($"[RawFTP] Test failed: {ex.Message}");
            return new FtpTestResult { Success = false, Message = ex.Message };
        }
        finally
        {
            control?.Dispose();
        }
    }

    /// <summary>
    /// Lists subdirectories at the given path on the FTP server.
    /// Uses raw TCP LIST command and parses entries containing &lt;DIR&gt;.
    /// </summary>
    public async Task<FtpBrowseResult> ListDirectoriesAsync(string host, string path)
    {
        _logger.Log($"[FtpWatcher] Browsing directories at {host}{path}...");

        TcpClient? control = null;
        try
        {
            var conn = await ConnectAndLoginAsync(host, path);
            if (conn == null)
                return new FtpBrowseResult { Success = false, Message = "Login or CWD failed" };

            control = conn.Value.control;
            var reader = conn.Value.reader;
            var writer = conn.Value.writer;

            var localIp = GetLocalAddressFor(host) ?? IPAddress.Any;
            var listData = await FtpActiveDataTransferAsync(writer, reader, localIp, "LIST", "LIST");
            if (listData == null)
                return new FtpBrowseResult { Success = false, Message = "Could not retrieve directory listing" };

            // Parse LIST output for directories.
            // Windows CE format: "01-15-26  10:30AM       <DIR>          FolderName"
            // Unix format:       "drwxr-xr-x  2 user group 4096 Jan 15 10:30 FolderName"
            var dirs = new List<FtpDirectoryEntry>();
            var lines = listData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var basePath = path.TrimEnd('/');

            foreach (var line in lines)
            {
                string? dirName = null;

                if (line.Contains("<DIR>"))
                {
                    // Windows CE / IIS format: everything after <DIR> whitespace is the name
                    var idx = line.IndexOf("<DIR>", StringComparison.OrdinalIgnoreCase);
                    dirName = line[(idx + 5)..].Trim();
                }
                else if (line.StartsWith("d", StringComparison.Ordinal))
                {
                    // Unix format: last token is the name
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 9)
                        dirName = string.Join(" ", parts.Skip(8));
                }

                if (!string.IsNullOrEmpty(dirName) && dirName != "." && dirName != "..")
                {
                    dirs.Add(new FtpDirectoryEntry
                    {
                        Name = dirName,
                        FullPath = $"{basePath}/{dirName}"
                    });
                }
            }

            await writer.WriteLineAsync("QUIT");

            _logger.Log($"[FtpWatcher] Found {dirs.Count} directories at {path}");
            return new FtpBrowseResult
            {
                Success = true,
                Message = $"Found {dirs.Count} directories",
                Directories = dirs
            };
        }
        catch (Exception ex)
        {
            _logger.Log($"[FtpWatcher] Browse failed: {ex.Message}");
            return new FtpBrowseResult { Success = false, Message = ex.Message };
        }
        finally
        {
            control?.Dispose();
        }
    }

    private void ForwardFile(string filename, byte[] content)
    {
        var payload = new
        {
            filename,
            content = Convert.ToBase64String(content),
            encoding = "base64",
            size = content.Length,
            siteId = _siteId,
            tenantId = _tenantId,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        _wsClient.EmitToServer("ftp:file", payload);
        Interlocked.Increment(ref _filesForwarded);
        _logger.Log($"[FtpWatcher:{Id}] Forwarded: {filename} ({content.Length} bytes)");
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
                    _logger.Log($"[FtpWatcher:{Id}] Loaded state: {_state.Files.Count} tracked files");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"[FtpWatcher:{Id}] Could not load state: {ex.Message}");
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
            _logger.Log($"[FtpWatcher:{Id}] Could not save state: {ex.Message}");
        }
    }
}

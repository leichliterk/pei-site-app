using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class FtpWatcher
{
    private FtpServerConfig _serverConfig;
    private readonly WebSocketClient _wsClient;
    private readonly PendingFileQueue _pendingQueue;
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

    public FtpWatcher(FtpServerConfig serverConfig, WebSocketClient wsClient, PendingFileQueue pendingQueue, int siteId, int tenantId, FileLogger logger)
    {
        _serverConfig = serverConfig;
        _wsClient = wsClient;
        _pendingQueue = pendingQueue;
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
                Name = _serverConfig.Name,
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

        _wsClient.FtpFileAckReceived += OnFtpFileAck;
        _wsClient.StatusChanged += OnWsStatusChanged;

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
            _wsClient.FtpFileAckReceived -= OnFtpFileAck;
            _wsClient.StatusChanged -= OnWsStatusChanged;
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

            var rawLines = nlstData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var fileNames = rawLines
                .Select(ParseNlstLine)
                .Where(f => f != null)
                .Select(f => f!)
                .ToArray();
            _logger.Log($"[FtpWatcher:{Id}] NLST returned {rawLines.Length} lines, {fileNames.Length} files parsed");
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

                // Use SIZE to detect new files and updates to existing files
                var sizeResp = await SendFtpCommandAsync(writer, reader, $"SIZE {fileName}", $"SIZE {fileName}");
                long remoteSize = -1;
                if (sizeResp != null && sizeResp.StartsWith("213") &&
                    long.TryParse(sizeResp[4..].Trim(), out var parsedSize))
                    remoteSize = parsedSize;

                bool isNew = existing == null;
                bool isChanged = existing != null && remoteSize >= 0 && remoteSize != existing.Size;

                if (!isNew && !isChanged) continue;

                _logger.Log($"[FtpWatcher:{Id}] {(isNew ? "New" : "Updated")} file: {fileName} " +
                            $"(remote={remoteSize} bytes, stored={existing?.Size.ToString() ?? "n/a"})");
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

            _lastResult = $"OK - {fileNames.Length} files listed, {newOrChanged} new/updated forwarded";
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
        // Compute SHA-256 hash for integrity verification
        string sha256Hash;
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(content);
            sha256Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var entry = new PendingFileEntry
        {
            PendingFileId = $"{Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}",
            ServerId = Id,
            Filename = filename,
            ContentBase64 = Convert.ToBase64String(content),
            Sha256 = sha256Hash,
            Size = content.Length,
            Source = string.IsNullOrEmpty(_serverConfig.Name) ? _serverConfig.FtpHost : _serverConfig.Name,
            SiteId = _siteId,
            TenantId = _tenantId,
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            QueuedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        // Save to pending queue FIRST (guarantees no data loss)
        _pendingQueue.Enqueue(entry);
        _logger.Log($"[FtpWatcher:{Id}] Queued: {filename} ({content.Length} bytes, SHA256: {sha256Hash[..12]}...)");

        // Attempt immediate send if WebSocket is connected; removal happens on ack
        if (_wsClient.Status == ConnectionStatus.connected)
        {
            _wsClient.EmitToServer("ftp:file", CreatePayloadFromEntry(entry));
            _logger.Log($"[FtpWatcher:{Id}] Sent, awaiting ack: {filename}");
        }
        else
        {
            _logger.Log($"[FtpWatcher:{Id}] WebSocket disconnected, file queued for later: {filename}");
        }

        Interlocked.Increment(ref _filesForwarded);
    }

    internal static object CreatePayloadFromEntry(PendingFileEntry entry)
    {
        return new
        {
            filename = entry.Filename,
            content = entry.ContentBase64,
            sha256 = entry.Sha256,
            encoding = entry.Encoding,
            size = entry.Size,
            source = entry.Source,
            siteId = entry.SiteId,
            tenantId = entry.TenantId,
            timestamp = entry.Timestamp
        };
    }

    /// <summary>
    /// Extracts a plain filename from an NLST response line.
    /// Handles two formats:
    ///   - Plain name (most servers):     "001870_251114_000500.DAE"
    ///   - Windows CE / IIS full listing: "11-15-25  00:00AM       11786 001870_251114_000500.DAE"
    ///   - Unix full listing:             "-rw-r--r-- 1 user grp 11786 Jan 15 00:00 001870_251114_000500.DAE"
    /// Returns null for directory entries (skipped during polling).
    /// </summary>
    private static string? ParseNlstLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line)) return null;

        // Windows CE / IIS: "MM-DD-YY  HH:MMAM  [<DIR>|size]  name"
        if (line.Length > 6 && char.IsDigit(line[0]) && line[2] == '-')
        {
            if (line.Contains("<DIR>")) return null; // skip directories
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            // parts: [0]=date [1]=time [2]=size [3..]=filename
            return parts.Length >= 4 ? string.Join(" ", parts.Skip(3)) : null;
        }

        // Unix: "drwx..." = dir (skip), "-rwx..." or "lrwx..." = file
        if (line[0] == 'd') return null;
        if (line[0] == '-' || line[0] == 'l')
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 9 ? string.Join(" ", parts.Skip(8)) : null;
        }

        // Plain filename
        return line;
    }

    private void OnFtpFileAck(FtpFileAck ack)
    {
        // Only handle acks for files belonging to this watcher
        var match = _pendingQueue.GetAll().FirstOrDefault(e => e.ServerId == Id && e.Filename == ack.Filename);
        if (match == null) return;

        if (ack.Success)
        {
            _pendingQueue.Remove(match.PendingFileId);
            _logger.Log($"[FtpWatcher:{Id}] ACK success: {ack.Filename} (server file_id={ack.FileId})");
        }
        else
        {
            _logger.Log($"[FtpWatcher:{Id}] ACK failure: {ack.Filename} - {ack.Error}. Clearing for re-poll.");
            // Remove from pending queue so we don't retry the stale queued data
            _pendingQueue.Remove(match.PendingFileId);
            // Remove from FTP state so the next poll cycle re-downloads and re-transmits fresh
            lock (_stateLock)
            {
                _state.Files.Remove(ack.Filename);
            }
            SaveState();
        }
    }

    private void OnWsStatusChanged(ConnectionStatus status)
    {
        if (status == ConnectionStatus.connected)
        {
            _logger.Log($"[FtpWatcher:{Id}] WebSocket reconnected, flushing own pending queue");
            FlushOwnPending();
        }
    }

    private void FlushOwnPending()
    {
        var entries = _pendingQueue.GetAll().Where(e => e.ServerId == Id).ToList();
        if (entries.Count == 0) return;

        _logger.Log($"[FtpWatcher:{Id}] Flushing {entries.Count} pending file(s)");
        foreach (var entry in entries)
        {
            if (_wsClient.Status != ConnectionStatus.connected) break;
            _wsClient.EmitToServer("ftp:file", CreatePayloadFromEntry(entry));
            _logger.Log($"[FtpWatcher:{Id}] Re-sent, awaiting ack: {entry.Filename}");
        }
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

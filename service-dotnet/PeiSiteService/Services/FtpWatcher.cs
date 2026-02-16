using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentFTP;
using PeiSiteService.Models;

namespace PeiSiteService.Services;

/// <summary>
/// Routes FluentFTP protocol-level log messages to our FileLogger.
/// </summary>
internal sealed class FtpFileLogAdapter : IFtpLogger
{
    private readonly FileLogger _logger;
    public FtpFileLogAdapter(FileLogger logger) => _logger = logger;
    public void Log(FtpLogEntry entry) => _logger.Log($"[FluentFTP] {entry.Message}");
}


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

    /// <summary>
    /// Determines the local IP address that can reach the given FTP host.
    /// This is critical for active mode FTP (PORT command) — the client must
    /// advertise the correct LAN IP so the server can connect back.
    /// </summary>
    private IPAddress? GetLocalAddressFor(string ftpHost)
    {
        try
        {
            // Resolve the FTP host to an IP
            var hostAddresses = Dns.GetHostAddresses(ftpHost);
            var targetIp = hostAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (targetIp == null) return null;

            // Create a UDP socket (no actual data sent) to determine which
            // local interface would be used to reach the target
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(targetIp, 21);
            var localEndpoint = socket.LocalEndPoint as IPEndPoint;
            var localIp = localEndpoint?.Address;
            _logger.Log($"[FtpWatcher] Local IP for reaching {ftpHost} ({targetIp}): {localIp}");
            return localIp;
        }
        catch (Exception ex)
        {
            _logger.Log($"[FtpWatcher] Could not determine local IP for {ftpHost}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates and configures an FTP client with all the settings needed
    /// for the embedded Windows CE FTP server (active mode, no FEAT, etc.)
    /// </summary>
    private AsyncFtpClient CreateFtpClient(string host, bool verbose = false)
    {
        var client = new AsyncFtpClient(host, "anonymous", "anonymous@");
        client.Config.EncryptionMode = FtpEncryptionMode.None;
        client.Config.ConnectTimeout = 15000;
        client.Config.DataConnectionConnectTimeout = 15000;
        client.Config.ReadTimeout = 15000;
        client.Config.CheckCapabilities = false;
        client.Config.DataConnectionType = FtpDataConnectionType.PORT;
        client.Config.SendHost = false;

        // Force the correct local IP in PORT commands so the FTP server
        // connects back to the right address (not 127.0.0.1 or wrong interface)
        var localIp = GetLocalAddressFor(host);
        if (localIp != null)
        {
            client.Config.AddressResolver = () => localIp.ToString();
            _logger.Log($"[FtpWatcher] Set AddressResolver to {localIp}");
        }

        if (verbose)
        {
            client.Logger = new FtpFileLogAdapter(_logger);
        }

        return client;
    }

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

    /// <summary>
    /// Sends an FTP command over the control channel, reads the response, and logs it.
    /// Returns the full response line (e.g. "200 PORT command successful.").
    /// </summary>
    private async Task<string?> SendFtpCommandAsync(StreamWriter writer, StreamReader reader, string command, string label)
    {
        await writer.WriteLineAsync(command);
        var resp = await reader.ReadLineAsync();
        _logger.Log($"[RawFTP] {label}: {resp}");
        return resp;
    }

    /// <summary>
    /// Opens an active-mode FTP data channel: creates a TcpListener, sends PORT,
    /// sends the given command (e.g. NLST or RETR), waits for the server to connect back,
    /// and returns the raw data as a string.
    /// </summary>
    private async Task<string?> FtpActiveDataTransferAsync(
        StreamWriter writer, StreamReader reader,
        IPAddress localIp, string command, string label)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(localIp, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Send PORT
            var ipBytes = localIp.GetAddressBytes();
            var portCmd = $"PORT {ipBytes[0]},{ipBytes[1]},{ipBytes[2]},{ipBytes[3]},{port / 256},{port % 256}";
            var resp = await SendFtpCommandAsync(writer, reader, portCmd, "PORT");
            if (resp == null || !resp.StartsWith("200")) return null;

            // Send the data command
            resp = await SendFtpCommandAsync(writer, reader, command, label);
            if (resp == null || !resp.StartsWith("150")) return null;

            // Accept the data connection (30s timeout for slow CE device)
            var acceptTask = listener.AcceptTcpClientAsync();
            var completed = await Task.WhenAny(acceptTask, Task.Delay(30000));
            if (completed != acceptTask)
            {
                _logger.Log($"[RawFTP] Timeout waiting for data connection ({label})");
                return null;
            }

            using var dataClient = acceptTask.Result;
            var dataStream = dataClient.GetStream();
            dataStream.ReadTimeout = 30000;

            using var dataReader = new StreamReader(dataStream, Encoding.ASCII);
            var data = await dataReader.ReadToEndAsync();

            // Read 226 Transfer complete
            resp = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP] {label} transfer: {resp}");

            return data;
        }
        finally
        {
            listener?.Stop();
        }
    }

    public async Task PollAsync()
    {
        if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0)
        {
            _logger.Log("[FtpWatcher] Poll already in progress, skipping");
            return;
        }

        TcpClient? control = null;
        try
        {
            _logger.Log($"[FtpWatcher] Connecting (raw TCP) to {_config.FtpHost}...");

            // 1. Connect control channel
            control = new TcpClient();
            await control.ConnectAsync(_config.FtpHost, 21);
            var stream = control.GetStream();
            stream.ReadTimeout = 15000;
            stream.WriteTimeout = 15000;
            var reader = new StreamReader(stream, Encoding.ASCII);
            var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            var banner = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP] Banner: {banner}");

            // 2. Login
            var resp = await SendFtpCommandAsync(writer, reader, "USER anonymous", "USER");
            resp = await SendFtpCommandAsync(writer, reader, "PASS anonymous@", "PASS");
            if (resp == null || !resp.StartsWith("230"))
            {
                _lastResult = $"Error: Login failed: {resp}";
                _logger.Log($"[FtpWatcher] {_lastResult}");
                return;
            }

            // 3. CWD to target directory
            var remotePath = _config.FtpPath.TrimEnd('/');
            resp = await SendFtpCommandAsync(writer, reader, $"CWD {remotePath}", "CWD");
            if (resp == null || !resp.StartsWith("250"))
            {
                _lastResult = $"Error: CWD failed: {resp}";
                _logger.Log($"[FtpWatcher] {_lastResult}");
                return;
            }

            // 4. Get file listing via NLST
            var localIp = GetLocalAddressFor(_config.FtpHost) ?? IPAddress.Any;
            var nlstData = await FtpActiveDataTransferAsync(writer, reader, localIp, "NLST", "NLST");
            if (nlstData == null)
            {
                _lastResult = "Error: Could not retrieve file listing";
                _logger.Log($"[FtpWatcher] {_lastResult}");
                return;
            }

            var fileNames = nlstData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _logger.Log($"[FtpWatcher] NLST returned {fileNames.Length} files");
            int newOrChanged = 0;

            // 5. Check each file against state and download new/changed ones
            foreach (var fileName in fileNames)
            {
                FtpFileState? existing;
                lock (_stateLock) { _state.Files.TryGetValue(fileName, out existing); }

                // With NLST we only get names, not sizes/dates — treat all untracked files as new
                if (existing != null) continue;

                _logger.Log($"[FtpWatcher] New file: {fileName}");

                try
                {
                    // Download file via RETR
                    var fileData = await FtpActiveDataTransferAsync(writer, reader, localIp, $"RETR {fileName}", $"RETR {fileName}");
                    if (fileData != null)
                    {
                        ForwardFile(fileName, fileData);
                        newOrChanged++;

                        lock (_stateLock)
                        {
                            _state.Files[fileName] = new FtpFileState
                            {
                                Name = fileName,
                                Size = fileData.Length,
                                ModifiedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"[FtpWatcher] Error downloading {fileName}: {ex.Message}");
                }
            }

            lock (_stateLock) { _state.LastPoll = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"); }
            SaveState();

            _lastResult = $"OK - {fileNames.Length} files listed, {newOrChanged} forwarded";
            _logger.Log($"[FtpWatcher] Poll complete: {_lastResult}");

            // 6. Quit
            await writer.WriteLineAsync("QUIT");
        }
        catch (Exception ex)
        {
            _lastResult = $"Error: {ex.Message}";
            _logger.Log($"[FtpWatcher] Poll failed: {_lastResult}");
        }
        finally
        {
            control?.Dispose();
            Interlocked.Exchange(ref _isPolling, 0);
        }
    }

    public async Task<FtpTestResult> TestConnectionAsync(string host, string remotePath)
    {
        // Use raw TCP sockets instead of FluentFTP for the test.
        // FluentFTP's active mode reads and closes the data socket too quickly
        // for the slow Windows CE FTP server, resulting in 0 items every time.
        _logger.Log($"[FtpWatcher] Testing connection (raw TCP) to {host}{remotePath}...");

        TcpClient? control = null;
        TcpListener? dataListener = null;
        try
        {
            // 1. Connect control channel
            control = new TcpClient();
            await control.ConnectAsync(host, 21);
            var stream = control.GetStream();
            stream.ReadTimeout = 15000;
            stream.WriteTimeout = 15000;
            var reader = new StreamReader(stream, Encoding.ASCII);
            var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            var banner = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP] Banner: {banner}");

            // 2. Login
            await writer.WriteLineAsync("USER anonymous");
            var resp = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP] USER: {resp}");

            await writer.WriteLineAsync("PASS anonymous@");
            resp = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP] PASS: {resp}");
            if (resp == null || !resp.StartsWith("230"))
                return new FtpTestResult { Success = false, Message = $"Login failed: {resp}" };

            // 3. CWD to the target directory
            await writer.WriteLineAsync($"CWD {remotePath.TrimEnd('/')}");
            resp = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP] CWD: {resp}");
            if (resp == null || !resp.StartsWith("250"))
                return new FtpTestResult { Success = false, Message = $"CWD failed: {resp}" };

            // 4. Set up data listener on a local port
            var localIp = GetLocalAddressFor(host) ?? IPAddress.Any;
            dataListener = new TcpListener(localIp, 0); // OS picks a free port
            dataListener.Start();
            var dataPort = ((IPEndPoint)dataListener.LocalEndpoint).Port;
            _logger.Log($"[RawFTP] Data listener on {localIp}:{dataPort}");

            // 5. Send PORT command
            var ipBytes = localIp.GetAddressBytes();
            var portHi = dataPort / 256;
            var portLo = dataPort % 256;
            var portCmd = $"PORT {ipBytes[0]},{ipBytes[1]},{ipBytes[2]},{ipBytes[3]},{portHi},{portLo}";
            _logger.Log($"[RawFTP] Sending: {portCmd}");
            await writer.WriteLineAsync(portCmd);
            resp = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP] PORT: {resp}");
            if (resp == null || !resp.StartsWith("200"))
                return new FtpTestResult { Success = false, Message = $"PORT failed: {resp}" };

            // 6. Send NLST command (simpler than LIST, just filenames)
            await writer.WriteLineAsync("NLST");
            resp = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP] NLST: {resp}");
            if (resp == null || !resp.StartsWith("150"))
                return new FtpTestResult { Success = false, Message = $"NLST failed: {resp}" };

            // 7. Wait for the server to connect back (generous timeout for slow CE device)
            _logger.Log("[RawFTP] Waiting for data connection from server...");
            var acceptTask = dataListener.AcceptTcpClientAsync();
            var completed = await Task.WhenAny(acceptTask, Task.Delay(30000));
            if (completed != acceptTask)
            {
                _logger.Log("[RawFTP] Timeout waiting for server to connect to data port");
                return new FtpTestResult { Success = false, Message = "Server did not connect to data port within 30 seconds. Firewall may be blocking inbound connections." };
            }

            using var dataClient = acceptTask.Result;
            var dataStream = dataClient.GetStream();
            dataStream.ReadTimeout = 30000;
            _logger.Log($"[RawFTP] Data connection accepted from {dataClient.Client.RemoteEndPoint}");

            // 8. Read all data with generous timeouts
            using var dataReader = new StreamReader(dataStream, Encoding.ASCII);
            var dataContent = await dataReader.ReadToEndAsync();
            _logger.Log($"[RawFTP] Data received: {dataContent.Length} chars");

            // Parse filenames from NLST output
            var fileNames = dataContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _logger.Log($"[RawFTP] Parsed {fileNames.Length} file names");
            if (fileNames.Length > 0)
            {
                _logger.Log($"[RawFTP] First 5: {string.Join(", ", fileNames.Take(5))}");
            }

            // 9. Read the 226 completion response
            resp = await reader.ReadLineAsync();
            _logger.Log($"[RawFTP] Transfer response: {resp}");

            // 10. Quit
            await writer.WriteLineAsync("QUIT");

            dataListener.Stop();

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
            dataListener?.Stop();
            control?.Dispose();
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

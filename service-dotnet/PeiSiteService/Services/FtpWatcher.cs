using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class FtpWatcher
{
    private FtpServerConfig _serverConfig;
    private readonly FileQueue _fileQueue;
    private readonly int _siteId;
    private readonly int _tenantId;
    private readonly FileLogger _logger;

    private readonly Action<string>? _onForceUploadComplete;

    private Timer? _pollTimer;
    private string? _lastPoll;
    private string _lastResult = "never polled";
    private int _filesEnqueued;
    private int _isPolling;
    private bool _paused;
    private readonly object _lock = new();

    public string Id => _serverConfig.Id;
    public string Host => _serverConfig.FtpHost;
    public bool IsCurrentlyPolling => _isPolling == 1;
    public bool IsPaused { get { lock (_lock) { return _paused; } } }

    public FtpWatcher(FtpServerConfig serverConfig, FileQueue fileQueue, int siteId, int tenantId, FileLogger logger, Action<string>? onForceUploadComplete = null)
    {
        _serverConfig = serverConfig;
        _fileQueue = fileQueue;
        _siteId = siteId;
        _tenantId = tenantId;
        _logger = logger;
        _onForceUploadComplete = onForceUploadComplete;
    }

    public FtpWatcherStatus GetStatus()
    {
        lock (_lock)
        {
            return new FtpWatcherStatus
            {
                Id = _serverConfig.Id,
                Name = _serverConfig.Name,
                Host = _serverConfig.FtpHost,
                Path = _serverConfig.FtpPath,
                Username = _serverConfig.Username,
                PollInterval = _serverConfig.FtpPollInterval,
                LastPoll = _lastPoll,
                LastResult = _lastResult,
                FilesForwarded = _filesEnqueued,
                IsPolling = _isPolling == 1,
                ForceFullUploadOnNextPoll = _serverConfig.ForceFullUploadOnNextPoll
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
            _logger.Log(ServiceLogLevel.Warning, $"[FtpWatcher:{Id}] Not starting - no host configured");
            return;
        }

        if (_pollTimer != null)
        {
            _logger.Log(ServiceLogLevel.Debug, $"[FtpWatcher:{Id}] Already running");
            return;
        }

        _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher:{Id}] Starting - host: {_serverConfig.FtpHost}, path: {_serverConfig.FtpPath}, interval: {_serverConfig.FtpPollInterval}s");

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
            _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher:{Id}] Stopped");
        }
    }

    public void Pause()
    {
        lock (_lock) { _paused = true; }
        Stop();
        _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher:{Id}] Paused");
    }

    public void Resume()
    {
        lock (_lock) { _paused = false; }
        Start();
        _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher:{Id}] Resumed");
    }

    public void TriggerFullUpload()
    {
        lock (_lock) { _serverConfig.ForceFullUploadOnNextPoll = true; }
        _ = PollAsync();
        _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher:{Id}] Full upload triggered");
    }

    // ── Raw TCP FTP helpers ──────────────────────────────────────────────────

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
        _logger.Log(ServiceLogLevel.Debug, $"[RawFTP:{Id}] {label}: {resp}");
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
                _logger.Log(ServiceLogLevel.Warning, $"[RawFTP:{Id}] Timeout waiting for data connection ({label})");
                return null;
            }

            using var dataClient = acceptTask.Result;
            var dataStream = dataClient.GetStream();
            dataStream.ReadTimeout = 30000;

            using var ms = new MemoryStream();
            await dataStream.CopyToAsync(ms);

            resp = await reader.ReadLineAsync();
            _logger.Log(ServiceLogLevel.Debug, $"[RawFTP:{Id}] {label} transfer: {resp}");

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

            var username = string.IsNullOrEmpty(_serverConfig.Username) ? "anonymous" : _serverConfig.Username;
            var password = string.IsNullOrEmpty(_serverConfig.Password) ? "anonymous@" : _serverConfig.Password;
            await SendFtpCommandAsync(writer, reader, $"USER {username}", "USER");
            var resp = await SendFtpCommandAsync(writer, reader, $"PASS {password}", "PASS");
            if (resp == null || !resp.StartsWith("230"))
            {
                control.Dispose();
                return null;
            }

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

    // ── Poll ─────────────────────────────────────────────────────────────────

    public async Task PollAsync()
    {
        if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0)
        {
            _logger.Log(ServiceLogLevel.Debug, $"[FtpWatcher:{Id}] Poll already in progress, skipping");
            return;
        }

        TcpClient? control = null;
        try
        {
            _logger.Log(ServiceLogLevel.Debug, $"[FtpWatcher:{Id}] Connecting to {_serverConfig.FtpHost}...");
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

            // Try LIST first (returns timestamps + sizes in one call).
            // Fall back to NLST if the server doesn't support LIST.
            var listData = await FtpActiveDataTransferAsync(writer, reader, localIp, "LIST", "LIST");
            bool usedList = listData != null;
            if (!usedList)
                listData = await FtpActiveDataTransferAsync(writer, reader, localIp, "NLST", "NLST");

            if (listData == null)
            {
                _lastResult = "Error: Could not retrieve file listing";
                return;
            }

            var rawLines = listData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            FtpListEntry[] entries;
            if (usedList)
            {
                entries = rawLines
                    .Select(ParseListEntry)
                    .Where(e => e != null)
                    .Select(e => e!)
                    .ToArray();
                _logger.Log(ServiceLogLevel.Debug, $"[FtpWatcher:{Id}] LIST returned {rawLines.Length} lines, {entries.Length} files parsed");
            }
            else
            {
                entries = rawLines
                    .Select(ParseNlstLine)
                    .Where(f => f != null)
                    .Select(f => new FtpListEntry(f!, null, null))
                    .ToArray();
                _logger.Log(ServiceLogLevel.Debug, $"[FtpWatcher:{Id}] NLST fallback returned {rawLines.Length} lines, {entries.Length} files parsed");
            }

            bool isFullUpload = _serverConfig.ForceFullUploadOnNextPoll;
            var today = DateTime.UtcNow.Date;
            var yesterday = today.AddDays(-1);

            var typeResp = await SendFtpCommandAsync(writer, reader, "TYPE I", "TYPE I");
            if (typeResp == null || !typeResp.StartsWith("200"))
            {
                _lastResult = "Error: Could not switch to binary transfer mode";
                return;
            }

            if (isFullUpload)
                _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher:{Id}] Full upload requested — downloading all {entries.Length} files");

            int enqueued = 0;
            int skipped = 0;
            foreach (var entry in entries)
            {
                if (!isFullUpload && !IsModifiedOnDate(entry.ModifiedAt, today) && !IsModifiedOnDate(entry.ModifiedAt, yesterday))
                    continue;

                var reason = isFullUpload ? "full upload" : "today/yesterday";
                _logger.Log(ServiceLogLevel.Debug, $"[FtpWatcher:{Id}] Downloading ({reason}): {entry.Name} (modified={entry.ModifiedAt})");
                try
                {
                    var fileBytes = await FtpActiveDataTransferBytesAsync(writer, reader, localIp, $"RETR {entry.Name}", $"RETR {entry.Name}");
                    if (fileBytes != null)
                    {
                        bool wasEnqueued = EnqueueFile(entry.Name, fileBytes, entry.ModifiedAt);
                        if (wasEnqueued) enqueued++;
                        else skipped++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(ServiceLogLevel.Error, $"[FtpWatcher:{Id}] Error downloading {entry.Name}: {ex.Message}");
                }
            }

            if (isFullUpload)
            {
                _serverConfig.ForceFullUploadOnNextPoll = false;
                _onForceUploadComplete?.Invoke(Id);
            }

            lock (_lock) { _lastPoll = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"); }

            var mode = isFullUpload ? "full upload" : "today+yesterday";
            _lastResult = $"OK - {entries.Length} files listed, {enqueued} enqueued, {skipped} duplicate ({mode})";
            _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher:{Id}] Poll complete: {_lastResult}");

            await writer.WriteLineAsync("QUIT");
        }
        catch (Exception ex)
        {
            _lastResult = $"Error: {ex.Message}";
            _logger.Log(ServiceLogLevel.Error, $"[FtpWatcher:{Id}] Poll failed: {_lastResult}");
        }
        finally
        {
            control?.Dispose();
            Interlocked.Exchange(ref _isPolling, 0);
        }
    }

    // ── Enqueue ───────────────────────────────────────────────────────────────

    private bool EnqueueFile(string filename, byte[] content, string? modifiedAt)
    {
        string sha256Hash;
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(content);
            sha256Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var entry = new QueueEntry
        {
            Id = $"{Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}",
            ServerId = Id,
            Filename = filename,
            FileDate = modifiedAt ?? "",
            Sha256 = sha256Hash,
            SizeBytes = content.LongLength,
            Source = string.IsNullOrEmpty(_serverConfig.Name) ? _serverConfig.FtpHost : _serverConfig.Name,
            SiteId = _siteId,
            TenantId = _tenantId,
            ContentBase64 = Convert.ToBase64String(content),
        };

        bool enqueued = _fileQueue.Enqueue(entry);
        if (enqueued)
            Interlocked.Increment(ref _filesEnqueued);
        return enqueued;
    }

    // ── Connection test / directory browse ────────────────────────────────────

    public async Task<FtpTestResult> TestConnectionAsync(string host, string remotePath)
    {
        _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher] Testing connection (raw TCP) to {host}{remotePath}...");

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
            _logger.Log(ServiceLogLevel.Debug, $"[RawFTP] Parsed {fileNames.Length} file names");

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
            _logger.Log(ServiceLogLevel.Error, $"[RawFTP] Test failed: {ex.Message}");
            return new FtpTestResult { Success = false, Message = ex.Message };
        }
        finally
        {
            control?.Dispose();
        }
    }

    /// <summary>Lists subdirectories at the given path on the FTP server.</summary>
    public async Task<FtpBrowseResult> ListDirectoriesAsync(string host, string path)
    {
        _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher] Browsing directories at {host}{path}...");

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

            var dirs = new List<FtpDirectoryEntry>();
            var lines = listData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var basePath = path.TrimEnd('/');

            foreach (var line in lines)
            {
                string? dirName = null;

                if (line.Contains("<DIR>"))
                {
                    var idx = line.IndexOf("<DIR>", StringComparison.OrdinalIgnoreCase);
                    dirName = line[(idx + 5)..].Trim();
                }
                else if (line.StartsWith("d", StringComparison.Ordinal))
                {
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

            _logger.Log(ServiceLogLevel.Info, $"[FtpWatcher] Found {dirs.Count} directories at {path}");
            return new FtpBrowseResult
            {
                Success = true,
                Message = $"Found {dirs.Count} directories",
                Directories = dirs
            };
        }
        catch (Exception ex)
        {
            _logger.Log(ServiceLogLevel.Error, $"[FtpWatcher] Browse failed: {ex.Message}");
            return new FtpBrowseResult { Success = false, Message = ex.Message };
        }
        finally
        {
            control?.Dispose();
        }
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────

    // Converts MDTM timestamp (YYYYMMDDHHmmss, UTC) to ISO 8601 (yyyy-MM-ddTHH:mm:ss.fffZ).
    // Falls back to returning the input unchanged if it's not in MDTM format.
    internal static string MdtmToIso8601(string mdtm)
    {
        if (mdtm.Length == 14 && mdtm.All(char.IsDigit) &&
            DateTime.TryParseExact(mdtm, "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            return dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
        return mdtm;
    }

    // Returns true if the 14-digit MDTM timestamp (YYYYMMDDHHmmss, UTC) falls on the given UTC date.
    private static bool IsModifiedOnDate(string? modifiedAt, DateTime date)
    {
        if (string.IsNullOrEmpty(modifiedAt) || modifiedAt.Length < 8) return false;
        return modifiedAt.StartsWith(date.ToString("yyyyMMdd"));
    }

    internal static string? ParseNlstLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line)) return null;

        if (line.Length > 6 && char.IsDigit(line[0]) && line[2] == '-')
        {
            if (line.Contains("<DIR>")) return null;
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 4 ? string.Join(" ", parts.Skip(3)) : null;
        }

        if (line[0] == 'd') return null;
        if (line[0] == '-' || line[0] == 'l')
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 9 ? string.Join(" ", parts.Skip(8)) : null;
        }

        return line;
    }

    internal static string? ParseMdtmTimestamp(string? response)
    {
        if (response == null || !response.StartsWith("213 ")) return null;
        var timestamp = response[4..].Trim();
        return timestamp.Length == 14 && timestamp.All(char.IsDigit) ? timestamp : null;
    }

    internal static FtpListEntry? ParseListEntry(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line)) return null;

        if (line.Length > 6 && char.IsDigit(line[0]) && line[2] == '-')
        {
            if (line.Contains("<DIR>")) return null;
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return null;
            var name = string.Join(" ", parts.Skip(3));
            long? size = long.TryParse(parts[2], out var sz) ? sz : null;
            var modifiedAt = ParseWindowsCeDateTime(parts[0], parts[1]);
            return new FtpListEntry(name, size, modifiedAt);
        }

        if (line[0] == 'd') return null;

        if (line[0] == '-' || line[0] == 'l')
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9) return null;
            var name = string.Join(" ", parts.Skip(8));
            long? size = long.TryParse(parts[4], out var sz) ? sz : null;
            var modifiedAt = ParseUnixDateTime(parts[5], parts[6], parts[7]);
            return new FtpListEntry(name, size, modifiedAt);
        }

        return new FtpListEntry(line, null, null);
    }

    internal static string? ParseWindowsCeDateTime(string datePart, string timePart)
    {
        try
        {
            var d = datePart.Split('-');
            if (d.Length != 3) return null;
            if (!int.TryParse(d[0], out var month)) return null;
            if (!int.TryParse(d[1], out var day)) return null;
            if (!int.TryParse(d[2], out var yearShort)) return null;
            var year = yearShort < 70 ? 2000 + yearShort : 1900 + yearShort;

            bool isPm = timePart.EndsWith("PM", StringComparison.OrdinalIgnoreCase);
            bool isAm = timePart.EndsWith("AM", StringComparison.OrdinalIgnoreCase);
            if (!isPm && !isAm) return null;

            var t = timePart[..^2].Split(':');
            if (t.Length != 2) return null;
            if (!int.TryParse(t[0], out var hour)) return null;
            if (!int.TryParse(t[1], out var minute)) return null;

            if (isPm && hour != 12) hour += 12;
            if (isAm && hour == 12) hour = 0;

            return $"{year:D4}{month:D2}{day:D2}{hour:D2}{minute:D2}00";
        }
        catch { return null; }
    }

    internal static string? ParseUnixDateTime(string month, string day, string yearOrTime)
    {
        try
        {
            var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                                  "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            var mi = Array.FindIndex(months, m => m.Equals(month, StringComparison.OrdinalIgnoreCase));
            if (mi < 0) return null;
            var monthNum = mi + 1;

            if (!int.TryParse(day.Trim(), out var dayNum)) return null;

            int year, hour = 0, minute = 0;
            if (yearOrTime.Contains(':'))
            {
                year = DateTime.UtcNow.Year;
                var t = yearOrTime.Split(':');
                if (!int.TryParse(t[0], out hour)) return null;
                if (!int.TryParse(t[1], out minute)) return null;
            }
            else
            {
                if (!int.TryParse(yearOrTime, out year)) return null;
            }

            return $"{year:D4}{monthNum:D2}{dayNum:D2}{hour:D2}{minute:D2}00";
        }
        catch { return null; }
    }
}

/// <summary>
/// Represents a file entry parsed from a LIST or NLST response.
/// ModifiedAt is a 14-digit MDTM-compatible string (YYYYMMDDHHmmss) or null if unknown.
/// Size is null when the listing format doesn't include file size.
/// </summary>
internal record FtpListEntry(string Name, long? Size, string? ModifiedAt);

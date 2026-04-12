using System.Text.Json;
using System.Text.Json.Serialization;
using PeiSiteService.Models;

namespace PeiSiteService.Services;

/// <summary>
/// Persistent, in-memory file upload queue backed by a single JSON file on disk.
/// Thread-safe. Deduplicates by SHA-256. Keeps the last 100 terminal entries as history.
/// </summary>
public class FileQueue
{
    private readonly string _queuePath;
    private readonly FileLogger _logger;
    private readonly object _lock = new();
    private List<QueueEntry> _entries = new();
    private const int MaxHistory = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public FileQueue(FileLogger logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dir = Path.Combine(appData, ServicePaths.DataDirName);
        try { Directory.CreateDirectory(dir); } catch { }
        _queuePath = Path.Combine(dir, "file-queue.json");
        Load();
    }

    // For unit testing: inject a custom path
    internal FileQueue(string queuePath, FileLogger logger)
    {
        _logger = logger;
        _queuePath = queuePath;
        Load();
    }

    // ── Enqueue ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds an entry to the queue. Returns false (skipped) if the same SHA-256 is
    /// already present with status Pending, Sending, Retrying, or Success.
    /// </summary>
    public bool Enqueue(QueueEntry entry)
    {
        lock (_lock)
        {
            var dupe = _entries.Any(e => e.Sha256 == entry.Sha256 &&
                (e.Status == QueueEntryStatus.Pending ||
                 e.Status == QueueEntryStatus.Sending ||
                 e.Status == QueueEntryStatus.Retrying ||
                 e.Status == QueueEntryStatus.Success));
            if (dupe)
            {
                _logger.Log(ServiceLogLevel.Debug, $"[FileQueue] Duplicate skipped: {entry.Filename} ({entry.Sha256[..12]}...)");
                return false;
            }

            entry.Status = QueueEntryStatus.Pending;
            entry.QueuedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            _entries.Add(entry);
            Save();
            _logger.Log(ServiceLogLevel.Info, $"[FileQueue] Enqueued: {entry.Filename} ({entry.SizeBytes} bytes, sha256={entry.Sha256[..12]}...)");
            return true;
        }
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>Returns the oldest Pending entry, or null if none.</summary>
    public QueueEntry? GetPending()
    {
        lock (_lock)
        {
            return _entries
                .Where(e => e.Status == QueueEntryStatus.Pending)
                .OrderBy(e => e.QueuedAt)
                .FirstOrDefault();
        }
    }

    /// <summary>Returns all entries (active + history), newest-first.</summary>
    public List<QueueEntry> GetAll()
    {
        lock (_lock)
        {
            return _entries.OrderByDescending(e => e.QueuedAt).ToList();
        }
    }

    public int PendingCount
    {
        get { lock (_lock) { return _entries.Count(e => e.Status == QueueEntryStatus.Pending); } }
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public void MarkSending(string id)
    {
        lock (_lock)
        {
            var e = _entries.FirstOrDefault(x => x.Id == id);
            if (e == null) return;
            e.Status = QueueEntryStatus.Sending;
            e.SentAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            Save();
        }
    }

    public void MarkSuccess(string id, int? recordsInserted)
    {
        lock (_lock)
        {
            var e = _entries.FirstOrDefault(x => x.Id == id);
            if (e == null) return;
            e.Status = QueueEntryStatus.Success;
            e.AckAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            e.RecordsInserted = recordsInserted;
            // Clear content now that upload succeeded — keeps history file small
            e.ContentBase64 = "";
            PruneHistory();
            Save();
            _logger.Log(ServiceLogLevel.Info, $"[FileQueue] Success: {e.Filename} (records={recordsInserted})");
        }
    }

    public void MarkFailed(string id, string error)
    {
        lock (_lock)
        {
            var e = _entries.FirstOrDefault(x => x.Id == id);
            if (e == null) return;
            e.RetryCount++;
            e.LastError = error;

            if (e.RetryCount >= e.MaxRetries)
            {
                e.Status = QueueEntryStatus.PermanentlyFailed;
                e.FailedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                e.ContentBase64 = "";
                PruneHistory();
                _logger.Log(ServiceLogLevel.Error, $"[FileQueue] Permanently failed: {e.Filename} after {e.RetryCount} attempts — {error}");
            }
            else
            {
                var delaySeconds = e.RetryCount switch { 1 => 30, 2 => 60, _ => 120 };
                e.Status = QueueEntryStatus.Retrying;
                e.RetryAfter = DateTime.UtcNow.AddSeconds(delaySeconds).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                _logger.Log(ServiceLogLevel.Warning, $"[FileQueue] Retry {e.RetryCount}/{e.MaxRetries} for {e.Filename} in {delaySeconds}s — {error}");
            }
            Save();
        }
    }

    /// <summary>
    /// Promotes any Retrying entries whose RetryAfter time has passed back to Pending.
    /// Called by FileBroker on each tick.
    /// </summary>
    public bool TickRetrying()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            bool changed = false;
            foreach (var e in _entries.Where(x => x.Status == QueueEntryStatus.Retrying))
            {
                if (e.RetryAfter != null &&
                    DateTime.TryParse(e.RetryAfter, null, System.Globalization.DateTimeStyles.RoundtripKind, out var retryAfter) &&
                    now >= retryAfter)
                {
                    e.Status = QueueEntryStatus.Pending;
                    e.RetryAfter = null;
                    changed = true;
                }
            }
            if (changed) Save();
            return changed;
        }
    }

    // ── Manual retry / history clear ──────────────────────────────────────────

    /// <summary>Resets a PermanentlyFailed entry back to Pending for manual retry.</summary>
    public bool RetryFailed(string id)
    {
        lock (_lock)
        {
            var e = _entries.FirstOrDefault(x => x.Id == id && x.Status == QueueEntryStatus.PermanentlyFailed);
            if (e == null) return false;
            e.Status = QueueEntryStatus.Pending;
            e.RetryCount = 0;
            e.LastError = null;
            e.FailedAt = null;
            e.RetryAfter = null;
            Save();
            _logger.Log(ServiceLogLevel.Info, $"[FileQueue] Manual retry: {e.Filename}");
            return true;
        }
    }

    /// <summary>Removes all terminal (Success / PermanentlyFailed) entries from history.</summary>
    public void ClearHistory()
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Status == QueueEntryStatus.Success || e.Status == QueueEntryStatus.PermanentlyFailed);
            Save();
            _logger.Log(ServiceLogLevel.Info, "[FileQueue] History cleared");
        }
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void PruneHistory()
    {
        // Called inside _lock — keep only the newest MaxHistory terminal entries
        var terminal = _entries
            .Where(e => e.Status == QueueEntryStatus.Success || e.Status == QueueEntryStatus.PermanentlyFailed)
            .OrderByDescending(e => e.AckAt ?? e.FailedAt ?? e.QueuedAt)
            .Skip(MaxHistory)
            .ToList();
        foreach (var old in terminal)
            _entries.Remove(old);
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            File.WriteAllText(_queuePath, json);
        }
        catch (Exception ex)
        {
            _logger.Log(ServiceLogLevel.Error, $"[FileQueue] Could not save queue: {ex.Message}");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_queuePath)) return;
            var json = File.ReadAllText(_queuePath);
            var loaded = JsonSerializer.Deserialize<List<QueueEntry>>(json, JsonOptions);
            if (loaded != null)
            {
                // Any entries that were mid-send when the service stopped should be reset to Pending
                foreach (var e in loaded.Where(x => x.Status == QueueEntryStatus.Sending))
                    e.Status = QueueEntryStatus.Pending;
                _entries = loaded;
                var pending = _entries.Count(e => e.Status == QueueEntryStatus.Pending);
                _logger.Log(ServiceLogLevel.Info, $"[FileQueue] Loaded {_entries.Count} entries ({pending} pending) from disk");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(ServiceLogLevel.Warning, $"[FileQueue] Could not load queue from disk, starting fresh: {ex.Message}");
            _entries = new();
        }
    }
}

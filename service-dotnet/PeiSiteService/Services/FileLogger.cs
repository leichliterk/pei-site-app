using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class FileLogger
{
    private readonly string _logDir;
    private readonly object _lock = new();
    private DateTime _lastPruneDate = DateTime.MinValue;
    private const int KeepDays = 30;
    private const int MaxBufferedEntries = 500;
    private readonly Queue<LogEntry> _recentEntries = new();

    public ServiceLogLevel MinLevel { get; set; } = ServiceLogLevel.Info;

    private static readonly string[] LevelLabels = { "DEBUG", "INFO ", "WARN ", "ERROR", "CRIT " };
    private static readonly string[] LevelNames  = { "debug", "info",  "warn",  "error", "crit"  };

    public FileLogger()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _logDir = Path.Combine(programData, "PEI Site Service", "logs");
        try { Directory.CreateDirectory(_logDir); } catch { }
    }

    public void Log(string message) => Log(ServiceLogLevel.Info, message);

    public void Log(ServiceLogLevel level, string message)
    {
        if (level < MinLevel) return;

        var now = DateTime.UtcNow;
        var label = LevelLabels[(int)level];
        var logMessage = $"[{now:yyyy-MM-ddTHH:mm:ss.fffZ}] [{label}] {message}";
        Console.WriteLine(logMessage);
        lock (_lock)
        {
            _recentEntries.Enqueue(new LogEntry
            {
                Timestamp = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                Level = LevelNames[(int)level],
                Message = message
            });
            if (_recentEntries.Count > MaxBufferedEntries)
                _recentEntries.Dequeue();

            try
            {
                var logFile = Path.Combine(_logDir, $"service-{now:yyyy-MM-dd}.log");
                File.AppendAllText(logFile, logMessage + Environment.NewLine);

                // Prune old log files once per calendar day
                if (now.Date > _lastPruneDate.Date)
                {
                    _lastPruneDate = now;
                    PruneOldLogs(now);
                }
            }
            catch { }
        }
    }

    public List<LogEntry> GetRecentEntries(string? since = null)
    {
        lock (_lock)
        {
            if (since == null)
                return _recentEntries.ToList();
            return _recentEntries
                .Where(e => string.Compare(e.Timestamp, since, StringComparison.Ordinal) > 0)
                .ToList();
        }
    }

    private void PruneOldLogs(DateTime now)
    {
        var cutoff = now.Date.AddDays(-KeepDays);
        try
        {
            foreach (var file in Directory.GetFiles(_logDir, "service-*.log"))
            {
                try
                {
                    var datePart = Path.GetFileNameWithoutExtension(file)["service-".Length..];
                    if (DateTime.TryParse(datePart, out var fileDate) && fileDate < cutoff)
                        File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
    }
}

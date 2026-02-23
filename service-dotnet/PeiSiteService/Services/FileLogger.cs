namespace PeiSiteService.Services;

public class FileLogger
{
    private readonly string _logDir;
    private readonly object _lock = new();
    private DateTime _lastPruneDate = DateTime.MinValue;
    private const int KeepDays = 30;

    public FileLogger()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _logDir = Path.Combine(programData, "PEI Site Service", "logs");
        try { Directory.CreateDirectory(_logDir); } catch { }
    }

    public void Log(string message)
    {
        var now = DateTime.UtcNow;
        var logMessage = $"[{now:yyyy-MM-ddTHH:mm:ss.fffZ}] {message}";
        Console.WriteLine(logMessage);
        lock (_lock)
        {
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

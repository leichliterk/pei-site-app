namespace PeiSiteService.Services;

public class FileLogger
{
    private readonly string _logDir;
    private readonly object _lock = new();

    public FileLogger()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _logDir = Path.Combine(programData, "PEI Site Service", "logs");
        try { Directory.CreateDirectory(_logDir); } catch { }
    }

    public void Log(string message)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var logMessage = $"[{timestamp}] {message}";
        Console.WriteLine(logMessage);
        lock (_lock)
        {
            try
            {
                var logFile = Path.Combine(_logDir, $"service-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(logFile, logMessage + Environment.NewLine);
            }
            catch { }
        }
    }
}

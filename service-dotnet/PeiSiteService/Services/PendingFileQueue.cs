using System.Text.Json;
using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class PendingFileQueue
{
    private readonly string _pendingDir;
    private readonly FileLogger _logger;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public PendingFileQueue(FileLogger logger)
    {
        _logger = logger;
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _pendingDir = Path.Combine(programData, "PEI Site Service", "pending");
        try
        {
            Directory.CreateDirectory(_pendingDir);
        }
        catch (Exception ex)
        {
            _logger.Log($"[PendingFileQueue] Could not create pending directory: {ex.Message}");
        }
    }

    public void Enqueue(PendingFileEntry entry)
    {
        lock (_lock)
        {
            try
            {
                var filePath = Path.Combine(_pendingDir, $"{entry.PendingFileId}.json");
                var json = JsonSerializer.Serialize(entry, JsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.Log($"[PendingFileQueue] Error enqueuing {entry.Filename}: {ex.Message}");
            }
        }
    }

    public List<PendingFileEntry> GetAll()
    {
        lock (_lock)
        {
            var entries = new List<PendingFileEntry>();
            try
            {
                if (!Directory.Exists(_pendingDir)) return entries;

                foreach (var file in Directory.GetFiles(_pendingDir, "*.json").OrderBy(f => f))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var entry = JsonSerializer.Deserialize<PendingFileEntry>(json, JsonOptions);
                        if (entry != null)
                            entries.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"[PendingFileQueue] Corrupted pending file {Path.GetFileName(file)}, removing: {ex.Message}");
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[PendingFileQueue] Error reading pending directory: {ex.Message}");
            }
            return entries;
        }
    }

    public void Remove(string pendingFileId)
    {
        lock (_lock)
        {
            try
            {
                var filePath = Path.Combine(_pendingDir, $"{pendingFileId}.json");
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                _logger.Log($"[PendingFileQueue] Error removing {pendingFileId}: {ex.Message}");
            }
        }
    }

    public int GetPendingCount()
    {
        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(_pendingDir)) return 0;
                return Directory.GetFiles(_pendingDir, "*.json").Length;
            }
            catch { return 0; }
        }
    }
}

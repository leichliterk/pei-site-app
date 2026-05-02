using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PeiSiteService.Models;

namespace PeiSiteService.Services;

/// <summary>
/// Handles the full unattended OTA upgrade lifecycle:
///   1. Download bootstrapper from signed URL
///   2. Verify SHA-256 checksum
///   3. Write ota-pending.json marker (survives service restart)
///   4. Launch installer in quiet mode
///   5. Tail WiX log file → emit progress lines via WebSocket
///   6. On next startup after MSI restarts the service, read pending marker
///      and emit ota:installed so the server knows the upgrade succeeded.
/// </summary>
public class OtaInstaller
{
    private readonly FileLogger _logger;
    private readonly string _dataDir;

    // Shared HttpClient — 10-minute timeout for large installers
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private volatile bool _installing;

    /// <summary>
    /// Non-null on startup when the previous run wrote ota-pending.json
    /// (i.e. the service was killed mid-install by the MSI).  WebSocketClient
    /// reads this once and emits ota:installed on first connect.
    /// </summary>
    public OtaPendingState? StartupPending { get; private set; }

    public bool IsInstalling => _installing;

    /// <summary>Fired when the install phase changes. Args: (releaseId, phase, message?)</summary>
    public event Action<string, string, string?>? PhaseChanged;

    /// <summary>Fired for each line read from the WiX installer log. Args: (releaseId, line)</summary>
    public event Action<string, string>? ProgressLine;

    public OtaInstaller(FileLogger logger)
    {
        _logger = logger;
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ServicePaths.DataDirName);
        CheckForStartupPending();
    }

    // -------------------------------------------------------------------------
    // Startup pending detection
    // -------------------------------------------------------------------------

    private void CheckForStartupPending()
    {
        var pendingPath = Path.Combine(_dataDir, "ota-pending.json");
        if (!File.Exists(pendingPath)) return;

        try
        {
            var json = File.ReadAllText(pendingPath);
            var state = JsonSerializer.Deserialize<OtaPendingState>(json, _jsonOptions);
            File.Delete(pendingPath);

            if (state != null && !string.IsNullOrEmpty(state.ReleaseId))
            {
                StartupPending = state;
                _logger.Log(ServiceLogLevel.Info,
                    $"[OtaInstaller] Detected completed OTA install for release '{state.ReleaseId}' — will emit ota:installed on connect");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(ServiceLogLevel.Warning, $"[OtaInstaller] Failed to read ota-pending.json: {ex.Message}");
            try { File.Delete(pendingPath); } catch { }
        }
    }

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Kicks off the install on a background thread.  Safe to call from a socket
    /// event handler.  Silently ignores a second call while one is already running.
    /// </summary>
    public void StartInstall(OtaInstallCommand cmd)
    {
        if (_installing)
        {
            _logger.Log(ServiceLogLevel.Warning,
                "[OtaInstaller] Install already in progress — ignoring duplicate command");
            return;
        }

        _logger.Log(ServiceLogLevel.Info,
            $"[OtaInstaller] Starting unattended install for release '{cmd.ReleaseId}'");
        _ = Task.Run(() => RunAsync(cmd));
    }

    // -------------------------------------------------------------------------
    // Core install pipeline
    // -------------------------------------------------------------------------

    private async Task RunAsync(OtaInstallCommand cmd)
    {
        _installing = true;

        try { Directory.CreateDirectory(_dataDir); } catch { }

        var installerPath = Path.Combine(_dataDir, "ota-installer.exe");
        var logPath       = Path.Combine(_dataDir, "ota-install.log");
        var pendingPath   = Path.Combine(_dataDir, "ota-pending.json");

        try
        {
            // ---- Download ----
            EmitPhase(cmd.ReleaseId, OtaPhase.Downloading, "Downloading installer...");
            await DownloadAsync(cmd.DownloadUrl, installerPath, cmd.ReleaseId);

            // ---- Verify checksum ----
            if (!string.IsNullOrWhiteSpace(cmd.Sha256))
            {
                EmitPhase(cmd.ReleaseId, OtaPhase.Verifying, "Verifying file integrity...");
                VerifySha256(installerPath, cmd.Sha256);
                EmitLine(cmd.ReleaseId, "SHA-256 checksum verified.");
            }

            // ---- Write pending marker BEFORE launching ----
            // If the MSI kills this service before the process exits, the marker
            // survives so the restarted service can emit ota:installed.
            WritePending(pendingPath, cmd);

            // Clear any leftover log from a previous run
            try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }

            // ---- Launch installer ----
            EmitPhase(cmd.ReleaseId, OtaPhase.Launching, "Starting installer process...");
            _logger.Log(ServiceLogLevel.Info,
                $"[OtaInstaller] Launching: {installerPath} /quiet /norestart /log \"{logPath}\"");

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName        = installerPath,
                Arguments       = $"/quiet /norestart /log \"{logPath}\"",
                UseShellExecute = true,   // required when running as SYSTEM
                CreateNoWindow  = true
            })!;

            // ---- Tail log while installer runs ----
            EmitPhase(cmd.ReleaseId, OtaPhase.Installing, "Installation in progress...");
            await TailLogAsync(logPath, cmd.ReleaseId, process);

            // If we reach here the process exited before the service was killed.
            // Exit code 3010 = success, reboot required (rare for a service).
            var exitCode = process.ExitCode;
            _logger.Log(ServiceLogLevel.Info, $"[OtaInstaller] Installer exited with code {exitCode}");

            if (exitCode is 0 or 3010)
            {
                EmitPhase(cmd.ReleaseId, OtaPhase.Restarting,
                    exitCode == 3010
                        ? "Installation complete. A system restart is required."
                        : "Installation complete. Service is restarting...");
                // Pending marker stays on disk — new service will emit ota:installed.
            }
            else
            {
                // Failed — remove pending marker so we don't report a false success.
                try { File.Delete(pendingPath); } catch { }
                EmitPhase(cmd.ReleaseId, OtaPhase.Failed,
                    $"Installer failed (exit code {exitCode}). Check {logPath} for details.");
            }
        }
        catch (Exception ex)
        {
            _logger.Log(ServiceLogLevel.Error, $"[OtaInstaller] Unhandled error: {ex.Message}");
            try { File.Delete(pendingPath); } catch { }
            EmitPhase(cmd.ReleaseId, OtaPhase.Failed, ex.Message);
        }
        finally
        {
            _installing = false;
        }
    }

    // -------------------------------------------------------------------------
    // Download with progress
    // -------------------------------------------------------------------------

    private async Task DownloadAsync(string url, string destPath, string releaseId)
    {
        _logger.Log(ServiceLogLevel.Info, $"[OtaInstaller] Downloading from: {url}");

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var src  = await response.Content.ReadAsStreamAsync();
        using var dest = File.Create(destPath);

        var    buf          = new byte[81920];
        long   downloaded   = 0;
        int    lastPctEmit  = -1;
        int    read;

        while ((read = await src.ReadAsync(buf)) > 0)
        {
            await dest.WriteAsync(buf.AsMemory(0, read));
            downloaded += read;

            if (totalBytes > 0)
            {
                int pct = (int)(downloaded * 100 / totalBytes);
                // Emit at 0, 10, 20 … 100%
                if (pct / 10 != lastPctEmit / 10)
                {
                    EmitLine(releaseId,
                        $"Downloading... {pct}% ({downloaded / 1024:N0} KB / {totalBytes / 1024:N0} KB)");
                    lastPctEmit = pct;
                }
            }
        }

        _logger.Log(ServiceLogLevel.Info,
            $"[OtaInstaller] Download complete: {downloaded / 1024:N0} KB");
        EmitLine(releaseId, $"Download complete ({downloaded / 1024:N0} KB).");
    }

    // -------------------------------------------------------------------------
    // SHA-256 verification
    // -------------------------------------------------------------------------

    private static void VerifySha256(string filePath, string expectedHex)
    {
        using var sha    = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash   = sha.ComputeHash(stream);
        var actual = Convert.ToHexString(hash); // uppercase hex

        if (!actual.Equals(expectedHex.Replace("-", "").ToUpperInvariant(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"SHA-256 mismatch — expected {expectedHex}, got {actual}");
        }
    }

    // -------------------------------------------------------------------------
    // Pending marker file
    // -------------------------------------------------------------------------

    private void WritePending(string pendingPath, OtaInstallCommand cmd)
    {
        try
        {
            var state = new OtaPendingState
            {
                ReleaseId = cmd.ReleaseId,
                Version   = cmd.Version ?? "",
                StartedAt = DateTime.UtcNow.ToString("o")
            };
            File.WriteAllText(pendingPath,
                JsonSerializer.Serialize(state, _jsonOptions));
            _logger.Log(ServiceLogLevel.Info, "[OtaInstaller] Wrote ota-pending.json");
        }
        catch (Exception ex)
        {
            _logger.Log(ServiceLogLevel.Warning,
                $"[OtaInstaller] Could not write ota-pending.json: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Log file tail
    // -------------------------------------------------------------------------

    private async Task TailLogAsync(string logPath, string releaseId, Process process)
    {
        // WiX may take a moment to create the log file — poll up to 15 s
        for (int i = 0; i < 30 && !File.Exists(logPath); i++)
        {
            if (process.HasExited) return;
            await Task.Delay(500);
        }

        if (!File.Exists(logPath))
        {
            _logger.Log(ServiceLogLevel.Warning,
                "[OtaInstaller] Log file never appeared — waiting for process to exit");
            await process.WaitForExitAsync();
            return;
        }

        // Open with ReadWrite|Delete share so WiX can keep writing while we read
        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        // Read until the process exits
        while (!process.HasExited)
        {
            var line = await reader.ReadLineAsync();
            if (line != null)
                EmitLine(releaseId, line);
            else
                await Task.Delay(150);
        }

        // Drain any lines written just before exit
        string? remaining;
        while ((remaining = await reader.ReadLineAsync()) != null)
            EmitLine(releaseId, remaining);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void EmitPhase(string releaseId, string phase, string? message)
    {
        _logger.Log(ServiceLogLevel.Info, $"[OtaInstaller] Phase: {phase} — {message}");
        PhaseChanged?.Invoke(releaseId, phase, message);
    }

    private void EmitLine(string releaseId, string line) =>
        ProgressLine?.Invoke(releaseId, line);
}

/// <summary>Phase name constants for ota:install_status events.</summary>
public static class OtaPhase
{
    public const string Downloading = "downloading";
    public const string Verifying   = "verifying";
    public const string Launching   = "launching";
    public const string Installing  = "installing";
    public const string Restarting  = "restarting";
    public const string Failed      = "failed";
}

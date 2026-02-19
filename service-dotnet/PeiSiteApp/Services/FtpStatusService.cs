using PeiSiteApp.Models;

namespace PeiSiteApp.Services;

public class FtpStatusService : IDisposable
{
    private readonly LocalServiceClient _localService;
    private Timer? _pollTimer;
    private FtpStatus _status = FtpStatus.Disabled;

    public event Action<FtpStatus>? StatusChanged;

    public FtpStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(value);
            }
        }
    }

    public FtpStatusService(LocalServiceClient localService)
    {
        _localService = localService;
    }

    public void StartPolling()
    {
        if (_pollTimer != null) return;

        _ = CheckStatusAsync();

        _pollTimer = new Timer(async _ =>
        {
            await CheckStatusAsync();
        }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    public void StopPolling()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private async Task CheckStatusAsync()
    {
        if (!_localService.IsServiceAvailable)
        {
            Status = FtpStatus.Disabled;
            return;
        }

        var ftpStatus = await _localService.FtpGetStatusAsync();
        if (ftpStatus == null)
        {
            Status = FtpStatus.Disabled;
            return;
        }

        if (!ftpStatus.FtpEnabled)
        {
            Status = FtpStatus.Disabled;
        }
        else if (ftpStatus.Servers.Count == 0)
        {
            Status = FtpStatus.Disconnected;
        }
        else if (ftpStatus.Servers.Any(s => s.LastResult.StartsWith("Error")))
        {
            Status = FtpStatus.Error;
        }
        else if (ftpStatus.Servers.Any(s => s.IsPolling))
        {
            Status = FtpStatus.Connecting;
        }
        else
        {
            Status = FtpStatus.Connected;
        }
    }

    public void Dispose()
    {
        StopPolling();
    }
}

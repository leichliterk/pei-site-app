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

        Status = ftpStatus.Status switch
        {
            "connected" => FtpStatus.Connected,
            "connecting" => FtpStatus.Connecting,
            "error" => FtpStatus.Error,
            "disabled" => FtpStatus.Disabled,
            _ => FtpStatus.Disconnected
        };
    }

    public void Dispose()
    {
        StopPolling();
    }
}

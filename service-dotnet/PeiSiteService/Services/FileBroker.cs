using PeiSiteService.Models;

namespace PeiSiteService.Services;

/// <summary>
/// Sends queued files to the server one at a time, strictly sequentially.
/// Awaits an ftp:file_ack before sending the next file.
/// Pauses automatically when the WebSocket is disconnected and resumes on reconnect.
/// </summary>
public class FileBroker
{
    private readonly FileQueue _queue;
    private readonly WebSocketClient _wsClient;
    private readonly FileLogger _logger;

    private readonly SemaphoreSlim _sem = new(1, 1);
    private volatile bool _paused;
    private volatile bool _stopped;
    private Timer? _tickTimer;
    private TaskCompletionSource<FtpFileAck>? _pendingAck;

    public bool IsPaused => _paused;
    public bool IsInFlight => _sem.CurrentCount == 0;

    public FileBroker(FileQueue queue, WebSocketClient wsClient, FileLogger logger)
    {
        _queue = queue;
        _wsClient = wsClient;
        _logger = logger;
    }

    public void Start()
    {
        _stopped = false;
        _wsClient.FtpFileAckReceived += OnAck;
        _wsClient.StatusChanged += OnStatusChanged;
        _tickTimer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _logger.Log(ServiceLogLevel.Info, "[FileBroker] Started");
    }

    public void Stop()
    {
        _stopped = true;
        _tickTimer?.Dispose();
        _tickTimer = null;
        _wsClient.FtpFileAckReceived -= OnAck;
        _wsClient.StatusChanged -= OnStatusChanged;
        // Cancel any in-flight ack wait
        _pendingAck?.TrySetCanceled();
        _logger.Log(ServiceLogLevel.Info, "[FileBroker] Stopped");
    }

    public void Pause()
    {
        _paused = true;
        _logger.Log(ServiceLogLevel.Info, "[FileBroker] Paused");
    }

    public void Resume()
    {
        _paused = false;
        _logger.Log(ServiceLogLevel.Info, "[FileBroker] Resumed");
    }

    // ── Tick ─────────────────────────────────────────────────────────────────

    private async Task TickAsync()
    {
        if (_stopped || _paused) return;
        if (_wsClient.Status != ConnectionStatus.connected) return;

        // Promote any retrying entries whose backoff has expired
        _queue.TickRetrying();

        // Only one send at a time — if semaphore is taken we're mid-flight, skip
        if (!await _sem.WaitAsync(0)) return;
        try
        {
            var entry = _queue.GetPending();
            if (entry == null) return;

            _logger.Log(ServiceLogLevel.Info, $"[FileBroker] Sending: {entry.Filename} (id={entry.Id}, sha256={entry.Sha256[..12]}...)");
            _queue.MarkSending(entry.Id);

            _pendingAck = new TaskCompletionSource<FtpFileAck>(TaskCreationOptions.RunContinuationsAsynchronously);
            _wsClient.EmitToServer("ftp:file", BuildPayload(entry));

            FtpFileAck ack;
            try
            {
                ack = await _pendingAck.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                _logger.Log(ServiceLogLevel.Warning, $"[FileBroker] Ack timeout for {entry.Filename}");
                _queue.MarkFailed(entry.Id, "ack timeout");
                return;
            }
            catch (OperationCanceledException)
            {
                return; // broker stopping
            }
            finally
            {
                _pendingAck = null;
            }

            if (ack.Success)
            {
                _queue.MarkSuccess(entry.Id, ack.RecordsInserted);
                _logger.Log(ServiceLogLevel.Info, $"[FileBroker] Ack success: {entry.Filename} (fileId={ack.FileId}, records={ack.RecordsInserted})");
            }
            else
            {
                var err = ack.Error ?? "server rejected";
                _logger.Log(ServiceLogLevel.Warning, $"[FileBroker] Ack failure: {entry.Filename} — {err}");
                _queue.MarkFailed(entry.Id, err);
            }
        }
        finally
        {
            _sem.Release();
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnAck(FtpFileAck ack)
    {
        _pendingAck?.TrySetResult(ack);
    }

    private void OnStatusChanged(ConnectionStatus status)
    {
        if (status == ConnectionStatus.connected)
            _logger.Log(ServiceLogLevel.Info, "[FileBroker] WebSocket connected — will resume sending");
        else
            _logger.Log(ServiceLogLevel.Info, $"[FileBroker] WebSocket {status} — sends paused until reconnected");
    }

    // ── Payload ───────────────────────────────────────────────────────────────

    internal static object BuildPayload(QueueEntry entry) => new
    {
        filename = entry.Filename,
        content = entry.ContentBase64,
        sha256 = entry.Sha256,
        encoding = entry.Encoding,
        size = entry.SizeBytes,
        source = entry.Source,
        siteId = entry.SiteId,
        tenantId = entry.TenantId,
        modifiedAt = string.IsNullOrEmpty(entry.FileDate) ? (string?)null : FtpWatcher.MdtmToIso8601(entry.FileDate)
    };
}

using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class NotificationManager
{
    private const int MaxNotifications = 50;
    private readonly List<ServiceNotification> _notifications = new();
    private readonly object _lock = new();

    public int UnreadCount
    {
        get { lock (_lock) { return _notifications.Count(n => !n.Read); } }
    }

    public void Add(ServiceNotification notification)
    {
        lock (_lock)
        {
            // Deduplicate by id
            if (_notifications.Any(n => n.Id == notification.Id)) return;

            _notifications.Insert(0, notification);

            // Trim oldest beyond cap
            while (_notifications.Count > MaxNotifications)
                _notifications.RemoveAt(_notifications.Count - 1);
        }
    }

    public List<ServiceNotification> GetAll()
    {
        lock (_lock) { return _notifications.ToList(); }
    }

    public bool MarkRead(string id)
    {
        lock (_lock)
        {
            var n = _notifications.FirstOrDefault(n => n.Id == id);
            if (n == null) return false;
            n.Read = true;
            return true;
        }
    }

    public void MarkAllRead()
    {
        lock (_lock)
        {
            foreach (var n in _notifications)
                n.Read = true;
        }
    }
}

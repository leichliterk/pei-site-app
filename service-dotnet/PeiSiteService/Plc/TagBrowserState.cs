namespace PeiSiteService.Plc;

/// <summary>
/// Singleton holding the latest discovered tag list.
/// TagBrowserService updates this; WebSocketClient and ApiServer read it.
/// </summary>
public class TagBrowserState
{
    private volatile IReadOnlyList<DiscoveredTag> _tags = Array.Empty<DiscoveredTag>();

    public IReadOnlyList<DiscoveredTag> Tags => _tags;

    public event Action? TagsUpdated;

    public void Update(IReadOnlyList<DiscoveredTag> tags)
    {
        _tags = tags;
        TagsUpdated?.Invoke();
    }
}

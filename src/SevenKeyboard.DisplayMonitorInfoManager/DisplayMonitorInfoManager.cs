// Copyright SevenKeyboard Ltd. (2026). MIT License.
namespace SevenKeyboard;

/// <summary>A shared monitor cache with its own hidden top-level message window.</summary>
public static class DisplayMonitorInfoManager
{
    private static readonly object Gate = new();
    private static MonitorCache? _cache;

    public static bool IsInitialized => Volatile.Read(ref _cache)?.IsRunning == true;

    public static void Initialize() => GetCache(out _);

    /// <summary>Returns an immutable, ordered snapshot. A forced call refreshes synchronously.</summary>
    public static IReadOnlyList<DisplayMonitorInfo> GetList(bool force = false)
    {
        MonitorCache cache = GetCache(out bool created);
        // Like AHK, first use enumerates only once, even when force is true.
        return force && !created ? cache.Refresh(DpiContext.Capture()) : cache.Snapshot;
    }

    private static MonitorCache GetCache(out bool created)
    {
        lock (Gate)
        {
            created = _cache?.IsRunning != true;
            if (created)
            {
                _cache?.Dispose();
                // Assign only after startup succeeds, allowing another call to retry.
                Volatile.Write(ref _cache, new MonitorCache(MonitorEnumeration.Read, DpiContext.Capture()));
            }
            return _cache!;
        }
    }
}
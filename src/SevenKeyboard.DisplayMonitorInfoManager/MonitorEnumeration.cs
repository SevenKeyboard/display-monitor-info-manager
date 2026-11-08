using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace SevenKeyboard;

internal static class MonitorEnumeration
{
    internal static IReadOnlyList<DisplayMonitorInfo> Read() => Collect(
        callback =>
        {
            ExceptionDispatchInfo? error = null;
            Native.MonitorEnumProc nativeCallback = (monitor, dc, rect, data) =>
            {
                try { return callback(monitor) ? 1 : 0; }
                catch (Exception exception)
                {
                    // Never let a managed exception unwind through a native callback.
                    error = ExceptionDispatchInfo.Capture(exception);
                    return 0;
                }
            };
            int result = Native.EnumDisplayMonitors(0, 0,
                Marshal.GetFunctionPointerForDelegate(nativeCallback), 0);
            GC.KeepAlive(nativeCallback);
            error?.Throw();
            return result != 0;
        },
        ReadMonitor);

    internal static IReadOnlyList<DisplayMonitorInfo> Collect(
        Func<Func<nint, bool>, bool> enumerate,
        Func<nint, DisplayMonitorInfo?> readMonitor)
    {
        var records = new List<DisplayMonitorInfo>();
        // A failed enumeration still returns the successfully collected records.
        _ = enumerate(handle =>
        {
            if (readMonitor(handle) is { } info)
                records.Add(info);
            return true; // A bad GetMonitorInfo must not stop subsequent monitors.
        });
        return records.AsReadOnly();
    }

    private static unsafe DisplayMonitorInfo? ReadMonitor(nint handle)
    {
        Native.MonitorInfoEx info = new() { Size = (uint)sizeof(Native.MonitorInfoEx) };
        if (Native.GetMonitorInfo(handle, ref info) == 0)
            return null;
        return new DisplayMonitorInfo(handle, info.Flags, new string(info.DeviceName),
            info.Monitor.ToRectangle(), info.WorkArea.ToRectangle());
    }
}
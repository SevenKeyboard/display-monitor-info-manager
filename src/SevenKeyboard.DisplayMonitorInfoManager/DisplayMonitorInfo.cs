// Copyright SevenKeyboard Ltd. (2026). MIT License.
namespace SevenKeyboard;

/// <summary>A rectangle in the coordinate space of the query's DPI context.</summary>
public readonly record struct MonitorRectangle(int Left, int Top, int Right, int Bottom);

/// <summary>An immutable monitor snapshot. HMonitor is borrowed and can become stale.</summary>
public sealed record DisplayMonitorInfo(
    nint HMonitor,
    uint Flags,
    string DeviceName,
    MonitorRectangle Monitor,
    MonitorRectangle WorkArea);
# DisplayMonitorInfoManager

C# monitor enumeration and a shared display-information cache. Version **2.0.0**,
namespace `SevenKeyboard`, targeting `net10.0-windows`.

## API

```csharp
using SevenKeyboard;

IReadOnlyList<DisplayMonitorInfo> monitors = DisplayMonitorInfoManager.GetList();
IReadOnlyList<DisplayMonitorInfo> refreshed = DisplayMonitorInfoManager.GetList(force: true);
```

- `DisplayMonitorInfoManager.Initialize()` initializes the shared cache; ordinary
  queries do this automatically. `IsInitialized` reports whether its worker is running.
- `GetList(bool force = false)` returns an immutable snapshot in enumeration order.
  List indexing follows ordinary .NET zero-based indexing.
- `DisplayMonitorInfo` is a sealed record with `nint HMonitor`, `uint Flags`,
  `string DeviceName`, `MonitorRectangle Monitor`, and `MonitorRectangle WorkArea`.
- `MonitorRectangle` is a readonly record struct with signed `int Left, Top, Right, Bottom`.
  Monitor handles are borrowed; old snapshots can contain stale handles after display changes.

## Behavior

The cache owns a background thread and an invisible top-level window. It needs no
application window, UI framework, or product setup. It refreshes only on
`WM_SETTINGCHANGE (0x001A)` with `wParam == SPI_SETWORKAREA (0x002F)`, or on an
explicit forced query. `WM_DISPLAYCHANGE` does not refresh it.

The initializing caller's DPI context is captured for window creation and the
initial enumeration. Forced refreshes temporarily use the forcing caller's
context on the worker. Work-area messages use the window's original context.
No per-monitor policy is imposed, and caller threads are not changed.

Enumeration skips failed `GetMonitorInfoW` calls and continues. If enumeration
itself fails, already-collected entries are returned, possibly an empty list.
A later force/work-area refresh retries enumeration. Each published list is a new
read-only snapshot; earlier snapshots remain unchanged. Concurrent forced calls
are serialized on the owner thread.

Unexpected startup failures propagate and leave initialization retryable.
Unexpected forced-query exceptions propagate without stopping the worker.
A fatal message-callback failure stops the owner and fails pending requests;
a later public query can initialize a new owner. Shutdown closes admission,
finishes an in-flight read, and fails queued requests without leaving waiters.
The public shared worker has process lifetime.

The native `MONITORINFOEXW` layout is internal; no separate structure package is
required. Version 2.0.0 replaces AHK mutable objects with typed immutable snapshots.

## Build and test

From this repository in PowerShell:

```powershell
$artifacts = Join-Path $PWD ('.artifacts/check-' + [guid]::NewGuid().ToString('N'))
dotnet build tests/DisplayMonitorInfoManager.Tests/DisplayMonitorInfoManager.Tests.csproj -c Release --artifacts-path $artifacts
dotnet "$artifacts/bin/DisplayMonitorInfoManager.Tests/release/DisplayMonitorInfoManager.Tests.dll"
```

Tests cover ordering, skipped records, partial enumeration, retries, immutable
snapshots, concurrent refreshes, native structure layout, and automatic shared
initialization. Native checks enumerate monitors read-only and send messages
only to test-owned hidden windows. DPI checks cover unaware, system-aware,
per-monitor v1/v2, and GDI-scaled contexts on current Windows 10/11.
They do not change system settings, move the mouse, or show windows.


To run the x86 checks, use a fresh artifacts directory, add
`-p:PlatformTarget=x86` to the build command, and run the DLL with
`C:/Program Files (x86)/dotnet/dotnet.exe`. For x64 use
`-p:PlatformTarget=x64` and the x64 runtime. NativeAOT x64 can be checked with
`dotnet publish tests/DisplayMonitorInfoManager.Tests/DisplayMonitorInfoManager.Tests.csproj -c Release -r win-x64 -p:PublishAot=true --artifacts-path $artifacts`,
then run the executable under `$artifacts/publish/DisplayMonitorInfoManager.Tests/release_win-x64`.
Source: the original AHK1.1 branch of this repository. Copyright SevenKeyboard
Ltd. (2025–2026). MIT License.
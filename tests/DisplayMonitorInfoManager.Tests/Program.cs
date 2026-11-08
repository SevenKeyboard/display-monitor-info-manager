using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SevenKeyboard;

internal static partial class Program
{
    private static int _checks;

    private static void Main()
    {
        EnumerationFailures();
        WindowMessagesAndDpi();
        StartupAndRefreshRetry();
        PendingRequestsDuringStop(fatalBroadcast: false);
        PendingRequestsDuringStop(fatalBroadcast: true);
        PublicNativeEnumeration();
        Console.WriteLine($"PASS ({RuntimeInformation.ProcessArchitecture}): {_checks} manager checks (deterministic, owned HWND, read-only native enumeration).");
    }

    private static DisplayMonitorInfo Info(nint handle) =>
        new(handle, handle == 20 ? 8u : 0, "monitor-" + handle,
            new(-1200, -100, 0, 900), new(-1200, -100, 0, 860));

    private static void EnumerationFailures()
    {
        var visited = new List<nint>();
        var records = MonitorEnumeration.Collect(callback =>
        {
            foreach (nint handle in new nint[] { 30, 20, 10 })
                Check(callback(handle), "callback must continue after a failed monitor");
            return false; // Native failure after partial success must retain successes.
        }, handle =>
        {
            visited.Add(handle);
            return handle == 20 ? null : Info(handle);
        });
        Check(visited.SequenceEqual(new nint[] { 30, 20, 10 }), "every monitor visited");
        Check(records.Select(x => x.HMonitor).SequenceEqual(new nint[] { 30, 10 }), "skip bad entry, preserve enumeration order");
        Check(records[0].Monitor.Left == -1200, "signed coordinates preserved");
        Check(records is not DisplayMonitorInfo[], "backing array is not exposed");
        Throws<NotSupportedException>(() => ((IList<DisplayMonitorInfo>)records)[0] = Info(99), "snapshot is immutable");
        Check(MonitorEnumeration.Collect(_ => false, Info).Count == 0, "failed enumeration can return empty");
        Check(MonitorEnumeration.Collect(callback => { callback(10); return true; }, _ => null).Count == 0,
            "all bad GetMonitorInfo calls yield empty");
    }

    private static void WindowMessagesAndDpi()
    {
        int reads = 0;
        var contexts = new ConcurrentQueue<nint>();
        MonitorCache cache;
        nint initial;
        using (new DpiContext(-2))
        {
            initial = DpiContext.Capture();
            cache = new MonitorCache(() =>
            {
                contexts.Enqueue(DpiContext.Capture());
                int generation = Interlocked.Increment(ref reads);
                return Array.AsReadOnly(new[] { Info(generation) });
            }, initial);
        }
        nint hwnd = cache.WindowHandle;
        try
        {
            Check(IsWindow(hwnd) != 0 && IsWindowVisible(hwnd) == 0, "owns an invisible HWND");
            Check(GetAncestor(hwnd, 2) == hwnd, "HWND is top-level");
            bool found = false;
            EnumWindowProc callback = (window, _) => { if (window == hwnd) found = true; return 1; };
            Check(EnumWindows(Marshal.GetFunctionPointerForDelegate(callback), 0) != 0, "enumerate top-level windows read-only");
            GC.KeepAlive(callback);
            Check(found, "HWND is not a message-only window");
            Check(SameDpi(contexts.Last(), initial), "initial enumeration uses initializing caller DPI");
            Check(SameDpi(GetWindowDpiAwarenessContext(hwnd), initial), "window captures initializing caller DPI");
            var first = cache.Snapshot;

            Send(hwnd, 0x007E, 0x002F); // WM_DISPLAYCHANGE must not refresh.
            Send(hwnd, 0x001A, 0);
            Send(hwnd, 0x001A, 0x0030);
            Check(reads == 1 && ReferenceEquals(first, cache.Snapshot), "only exact setting/work-area pair refreshes");

            Send(hwnd, 0x001A, 0x002F);
            Check(reads == 2 && cache.Snapshot[0].HMonitor == 2, "matching message refreshes synchronously");
            Check(first[0].HMonitor == 1, "previous snapshot remains unchanged");

            foreach (nint context in new nint[] { -1, -2, -3, -4, -5 })
            {
                using var caller = new DpiContext(context);
                nint before = DpiContext.Capture();
                var refreshed = cache.Refresh(before);
                Check(SameDpi(contexts.Last(), before), $"forced query keeps caller DPI {context}");
                Check(SameDpi(DpiContext.Capture(), before), "caller DPI stays unchanged");
                Check(refreshed[0].HMonitor == reads, "force returns completed snapshot");
            }
            Send(hwnd, 0x001A, 0x002F);
            Check(SameDpi(contexts.Last(), initial), "broadcast returns to original window DPI after forced queries");

            int beforeConcurrent = reads;
            Task[] callers = Enumerable.Range(0, 8).Select(_ => Task.Run(() => cache.Refresh(DpiContext.Capture()))).ToArray();
            Check(Task.WaitAll(callers, TimeSpan.FromSeconds(10)), "concurrent forced queries complete");
            Check(reads == beforeConcurrent + 8, "each forced query enumerates exactly once");
        }
        finally { cache.Dispose(); }
        Check(IsWindow(hwnd) == 0 && !cache.IsRunning, "dispose destroys owned HWND and joins worker");
        cache.Dispose();
    }

    private static void StartupAndRefreshRetry()
    {
        Throws<InvalidOperationException>(() =>
        {
            using var failed = new MonitorCache(() => throw new InvalidOperationException("startup fixture"), DpiContext.Capture());
        }, "startup errors propagate after cleanup");

        int attempt = 0;
        using var cache = new MonitorCache(() =>
        {
            int generation = ++attempt;
            if (generation == 2)
                throw new InvalidOperationException("refresh fixture");
            if (generation == 4)
                return MonitorEnumeration.Collect(_ => false, Info);
            return MonitorEnumeration.Collect(callback =>
            {
                callback(10);
                callback(20);
                callback(30);
                return false;
            }, handle => generation == 1 && handle == 20 ? null : Info(handle));
        }, DpiContext.Capture());
        var initial = cache.Snapshot;
        Check(initial.Count == 2, "worker publishes partial initial enumeration");
        Throws<InvalidOperationException>(() => cache.Refresh(DpiContext.Capture()), "unexpected forced errors propagate");
        Check(cache.IsRunning, "failed forced query is retryable");
        var retried = cache.Refresh(DpiContext.Capture());
        Check(retried.Count == 3 && retried[1].HMonitor == 20, "later refresh retries failed monitor");
        Check(initial.Count == 2, "retry does not modify previous partial snapshot");
        Check(cache.Refresh(DpiContext.Capture()).Count == 0, "empty failed enumeration replaces previous cache");
        Check(retried.Count == 3, "empty refresh preserves earlier returned snapshot");
    }

    private static void PendingRequestsDuringStop(bool fatalBroadcast)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        using var cache = new MonitorCache(() =>
        {
            if (Interlocked.Increment(ref reads) == 2)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Blocked enumeration fixture timed out.");
                if (fatalBroadcast)
                    throw new InvalidOperationException("broadcast fixture");
            }
            return Array.AsReadOnly(new[] { Info(10) });
        }, DpiContext.Capture());
        nint hwnd = cache.WindowHandle;
        Task<Exception?> active = StartObserved(fatalBroadcast
            ? () =>
            {
                if (SendMessageTimeout(hwnd, 0x001A, 0x002F, 0, 0x0003, 10000, out _) == 0)
                    throw new InvalidOperationException("Owned work-area message timed out.");
            }
            : () => cache.Refresh(DpiContext.Capture()));
        try
        {
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "worker reached blocked read");
            Task<Exception?>[] pending = Enumerable.Range(0, 3)
                .Select(_ => StartObserved(() => cache.Refresh(DpiContext.Capture()))).ToArray();
            Check(SpinWait.SpinUntil(() => cache.PendingRequestCount == 3, TimeSpan.FromSeconds(5)),
                "all pending requests admitted before shutdown");
            Task<Exception?>? stopping = null;
            if (!fatalBroadcast)
            {
                stopping = StartObserved(cache.Dispose);
                Check(SpinWait.SpinUntil(() => !cache.IsRunning, TimeSpan.FromSeconds(5)), "dispose closes request admission");
                Throws<InvalidOperationException>(() => cache.Refresh(DpiContext.Capture()), "stopped worker rejects new requests");
            }
            release.Set();
            Check(Task.WaitAll(pending, TimeSpan.FromSeconds(5)) && active.Wait(TimeSpan.FromSeconds(5)),
                "active and pending callers complete without hanging");
            Check(active.Result is null, "in-flight operation completes");
            foreach (Task<Exception?> request in pending)
                Check(fatalBroadcast
                    ? request.Result is InvalidOperationException { Message: "broadcast fixture" }
                    : request.Result is ObjectDisposedException,
                    "pending requests receive the worker failure or shutdown error");
            if (stopping is not null)
                Check(stopping.Wait(TimeSpan.FromSeconds(5)) && stopping.Result is null, "dispose joins the worker");
        }
        finally
        {
            release.Set();
            cache.Dispose();
        }
        Check(!cache.IsRunning && IsWindow(hwnd) == 0 && cache.PendingRequestCount == 0,
            "stopped worker releases its HWND and drains pending requests");
    }

    private static Task<Exception?> StartObserved(Action action) => Task.Factory.StartNew(() =>
    {
        try { action(); return null; }
        catch (Exception exception) { return exception; }
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    private static unsafe void PublicNativeEnumeration()
    {
        Check(sizeof(Native.MonitorInfoEx) == 104, "MONITORINFOEXW layout is 104 bytes");
        Check(sizeof(Native.WindowClass) == (IntPtr.Size == 8 ? 80 : 48), "WNDCLASSEX layout matches process architecture");
        Check(sizeof(Native.Message) == (IntPtr.Size == 8 ? 48 : 32), "MSG layout matches process architecture");
        Check(!DisplayMonitorInfoManager.IsInitialized, "public cache starts lazy");
        var first = DisplayMonitorInfoManager.GetList(force: true);
        Check(DisplayMonitorInfoManager.IsInitialized, "first forced call initializes shared cache");
        DisplayMonitorInfoManager.Initialize();
        Check(ReferenceEquals(first, DisplayMonitorInfoManager.GetList()), "nonforced calls and Initialize reuse cache");
        var refreshed = DisplayMonitorInfoManager.GetList(force: true);
        Check(!ReferenceEquals(first, refreshed), "force publishes a distinct snapshot");
        foreach (var info in refreshed)
        {
            Check(info.HMonitor != 0 && info.DeviceName.Length > 0, "native record has handle and device name");
            Check(info.Monitor.Right >= info.Monitor.Left && info.Monitor.Bottom >= info.Monitor.Top, "native bounds are valid");
        }
        Console.WriteLine($"Read-only native enumeration: {refreshed.Count} monitor(s).");
    }

    private static bool SameDpi(nint left, nint right) => AreDpiAwarenessContextsEqual(left, right) != 0;

    private static void Send(nint window, uint message, nuint wParam) =>
        Check(SendMessageTimeout(window, message, wParam, 0, 0x0003, 5000, out _) != 0, "owned-window message dispatch");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { _checks++; return; }
        throw new InvalidOperationException(message);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumWindowProc(nint window, nint data);
    [LibraryImport("user32.dll")] private static partial int IsWindow(nint window);
    [LibraryImport("user32.dll")] private static partial int IsWindowVisible(nint window);
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint window, uint flags);
    [LibraryImport("user32.dll")] private static partial int EnumWindows(nint callback, nint data);
    [LibraryImport("user32.dll")] private static partial nint GetWindowDpiAwarenessContext(nint window);
    [LibraryImport("user32.dll")] private static partial int AreDpiAwarenessContextsEqual(nint left, nint right);
    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static partial nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, out nuint result);
}
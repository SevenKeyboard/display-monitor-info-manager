using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SevenKeyboard;

// One owner for enumeration and window lifetime. Public callers receive only immutable snapshots.
internal sealed class MonitorCache : IDisposable
{
    private const uint RefreshMessage = 0x8001;
    private const uint StopMessage = 0x8002;
    private readonly Func<IReadOnlyList<DisplayMonitorInfo>> _read;
    private readonly nint _initialDpiContext;
    private readonly Native.WindowProc _procedure;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<RefreshRequest> _requests = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<DisplayMonitorInfo> _snapshot = Array.AsReadOnly(Array.Empty<DisplayMonitorInfo>());
    private nint _window;
    private int _running;
    private Exception? _failure;

    internal MonitorCache(Func<IReadOnlyList<DisplayMonitorInfo>> read, nint initialDpiContext)
    {
        _read = read;
        _initialDpiContext = initialDpiContext;
        _procedure = WindowProcedure;
        _thread = new Thread(Run) { IsBackground = true, Name = nameof(DisplayMonitorInfoManager) };
        _thread.Start();
        try { _ready.Task.GetAwaiter().GetResult(); }
        catch
        {
            _thread.Join();
            throw;
        }
    }

    internal bool IsRunning => Volatile.Read(ref _running) != 0;
    internal IReadOnlyList<DisplayMonitorInfo> Snapshot => Volatile.Read(ref _snapshot);
    internal nint WindowHandle => _window;
    internal int PendingRequestCount => _requests.Count;

    internal IReadOnlyList<DisplayMonitorInfo> Refresh(nint callerDpiContext)
    {
        var request = new RefreshRequest(callerDpiContext);
        lock (_gate)
        {
            if (!IsRunning)
                throw new InvalidOperationException("The monitor cache thread has stopped.", _failure);
            _requests.Enqueue(request);
            if (Native.PostMessage(_window, RefreshMessage, 0, 0) == 0)
                request.Completion.TrySetException(new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        return request.Completion.Task.GetAwaiter().GetResult();
    }

    private unsafe void Run()
    {
        string className = "";
        nint instance = 0;
        bool registered = false;
        try
        {
            className = "SevenKeyboard.DisplayMonitorInfoManager." + Guid.NewGuid().ToString("N");
            instance = Native.GetModuleHandle(null);
            using var context = new DpiContext(_initialDpiContext);
            fixed (char* name = className)
            {
                Native.WindowClass windowClass = new()
                {
                    Size = (uint)sizeof(Native.WindowClass),
                    Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
                    Instance = instance,
                    ClassName = (nint)name
                };
                if (Native.RegisterClassEx(ref windowClass) == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                registered = true;
            }
            // A hidden top-level window receives broadcasts; HWND_MESSAGE would not.
            _window = Native.CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, 0, 0, instance, 0);
            if (_window == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            Publish();
            lock (_gate) { Volatile.Write(ref _running, 1); }
            _ready.SetResult();

            int result;
            while ((result = Native.GetMessage(out Native.Message message, 0, 0, 0)) > 0)
            {
                _ = Native.TranslateMessage(in message);
                _ = Native.DispatchMessage(in message);
            }
            if (result < 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        catch (Exception exception) { _failure = exception; }
        finally
        {
            lock (_gate)
            {
                Volatile.Write(ref _running, 0);
                Exception error = _failure ?? new ObjectDisposedException(nameof(MonitorCache));
                while (_requests.TryDequeue(out RefreshRequest? request))
                    request.Completion.TrySetException(error);
            }
            try
            {
                if (_window != 0)
                    _ = Native.DestroyWindow(_window);
                if (registered)
                    _ = Native.UnregisterClass(className, instance);
            }
            catch (Exception exception) { _failure ??= exception; }
            // Keep the reverse P/Invoke delegate alive until the HWND and class are gone.
            GC.KeepAlive(_procedure);
            _ready.TrySetException(_failure ?? new InvalidOperationException("The monitor cache stopped during initialization."));
        }
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == Native.SettingChange && wParam == Native.SetWorkArea && IsRunning)
            {
                // Windows enters this callback in the HWND's original DPI context.
                Publish();
                return 0;
            }
            if (message == RefreshMessage)
            {
                while (IsRunning && _requests.TryDequeue(out RefreshRequest? request))
                {
                    if (request.Completion.Task.IsCompleted)
                        continue;
                    try
                    {
                        IReadOnlyList<DisplayMonitorInfo> snapshot;
                        using (new DpiContext(request.DpiContext))
                            snapshot = Publish();
                        request.Completion.TrySetResult(snapshot);
                    }
                    catch (Exception exception) { request.Completion.TrySetException(exception); }
                }
                return 0;
            }
            if (message == StopMessage || message == 0x0002 /* WM_DESTROY */)
            {
                Volatile.Write(ref _running, 0);
                Native.PostQuitMessage(0);
                return 0;
            }
            return Native.DefWindowProc(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            // Unrecoverable callback errors stop the owner; a later public call can reinitialize.
            _failure = exception;
            Volatile.Write(ref _running, 0);
            Native.PostQuitMessage(1);
            return 0;
        }
    }

    private IReadOnlyList<DisplayMonitorInfo> Publish()
    {
        IReadOnlyList<DisplayMonitorInfo> snapshot = _read();
        Volatile.Write(ref _snapshot, snapshot);
        return snapshot;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                if (Native.PostMessage(_window, StopMessage, 0, 0) == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                Volatile.Write(ref _running, 0);
            }
        }
        if (Thread.CurrentThread != _thread)
            _thread.Join();
    }

    private sealed class RefreshRequest(nint dpiContext)
    {
        internal nint DpiContext { get; } = dpiContext;
        internal TaskCompletionSource<IReadOnlyList<DisplayMonitorInfo>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
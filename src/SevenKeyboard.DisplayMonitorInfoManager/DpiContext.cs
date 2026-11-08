using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SevenKeyboard;

// Reproduce the query's context, never select a different awareness policy.
internal readonly struct DpiContext : IDisposable
{
    private readonly nint _previous;

    internal static nint Capture() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)
            ? Native.GetThreadDpiAwarenessContext() : 0;

    internal DpiContext(nint context)
    {
        _previous = 0;
        if (context != 0)
        {
            _previous = Native.SetThreadDpiAwarenessContext(context);
            if (_previous == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public void Dispose()
    {
        if (_previous != 0 && Native.SetThreadDpiAwarenessContext(_previous) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
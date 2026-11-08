using System.Runtime.InteropServices;

namespace SevenKeyboard;

internal static partial class Native
{
    internal const uint SettingChange = 0x001A;
    internal const nuint SetWorkArea = 0x002F;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate int MonitorEnumProc(nint monitor, nint dc, nint rect, nint data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left, Top, Right, Bottom;
        internal readonly MonitorRectangle ToRectangle() => new(Left, Top, Right, Bottom);
    }

    // MONITORINFOEXW is 104 bytes on x86 and x64, with a 32-WCHAR device name.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MonitorInfoEx
    {
        internal uint Size;
        internal Rect Monitor, WorkArea;
        internal uint Flags;
        internal fixed char DeviceName[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowClass
    {
        internal uint Size, Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background, MenuName, ClassName, SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X, Y;
        internal uint Private;
    }

    [LibraryImport("user32.dll")]
    internal static partial int EnumDisplayMonitors(nint dc, nint clip, nint callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    internal static partial int GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [LibraryImport("user32.dll")]
    internal static partial nint GetThreadDpiAwarenessContext();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? name);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(ref WindowClass windowClass);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int UnregisterClass(string className, nint instance);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    internal static partial int DestroyWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(out Message message, nint window, uint minimum, uint maximum);

    [LibraryImport("user32.dll")]
    internal static partial int TranslateMessage(in Message message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in Message message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    internal static partial int PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);
}
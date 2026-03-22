using System.Runtime.InteropServices;

namespace Hypersonic.Interop;

internal sealed class WindowSubclass : IDisposable
{
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _subclassProc;
    private readonly Func<uint, IntPtr, IntPtr, (bool handled, IntPtr result)> _callback;
    private bool _disposed;

    public WindowSubclass(IntPtr hwnd, Func<uint, IntPtr, IntPtr, (bool handled, IntPtr result)> callback)
    {
        _hwnd = hwnd;
        _callback = callback;
        _subclassProc = SubclassProcHandler;
        SetWindowSubclass(_hwnd, _subclassProc, 0, 0);
    }

    private IntPtr SubclassProcHandler(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData)
    {
        var (handled, result) = _callback(uMsg, wParam, lParam);
        if (handled)
        {
            return result;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            RemoveWindowSubclass(_hwnd, _subclassProc, 0);
        }
    }
}

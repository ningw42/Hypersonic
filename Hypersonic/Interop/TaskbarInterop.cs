using System.Runtime.InteropServices;

namespace Hypersonic.Interop;

[ComImport]
[Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList3
{
    // ITaskbarList
    [PreserveSig] int HrInit();
    [PreserveSig] int AddTab(IntPtr hwnd);
    [PreserveSig] int DeleteTab(IntPtr hwnd);
    [PreserveSig] int ActivateTab(IntPtr hwnd);
    [PreserveSig] int SetActiveAlt(IntPtr hwnd);

    // ITaskbarList2
    [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

    // ITaskbarList3
    [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
    [PreserveSig] int SetProgressState(IntPtr hwnd, int tbpFlags);
    [PreserveSig] int RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
    [PreserveSig] int UnregisterTab(IntPtr hwndTab);
    [PreserveSig] int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
    [PreserveSig] int SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
    [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] THUMBBUTTON[] pButtons);
    [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] THUMBBUTTON[] pButtons);
    [PreserveSig] int ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
    [PreserveSig] int SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);
    [PreserveSig] int SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? pszTip);
    [PreserveSig] int SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct THUMBBUTTON
{
    public THUMBBUTTONMASK dwMask;
    public uint iId;
    public uint iBitmap;
    public IntPtr hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szTip;
    public THUMBBUTTONFLAGS dwFlags;
}

[Flags]
internal enum THUMBBUTTONMASK : uint
{
    THB_BITMAP = 0x0001,
    THB_ICON = 0x0002,
    THB_TOOLTIP = 0x0004,
    THB_FLAGS = 0x0008,
}

[Flags]
internal enum THUMBBUTTONFLAGS : uint
{
    THBF_ENABLED = 0x0000,
    THBF_DISABLED = 0x0001,
    THBF_DISMISSONCLICK = 0x0002,
    THBF_NOBACKGROUND = 0x0004,
    THBF_HIDDEN = 0x0008,
    THBF_NONINTERACTIVE = 0x0010,
}

internal static class TaskbarNativeMethods
{
    internal static readonly Guid CLSID_TaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    // Window messages
    internal const uint WM_COMMAND = 0x0111;

    // Thumbnail button IDs
    internal const uint BUTTON_PREVIOUS = 0;
    internal const uint BUTTON_PLAYPAUSE = 1;
    internal const uint BUTTON_NEXT = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint RegisterWindowMessageW([MarshalAs(UnmanagedType.LPWStr)] string lpString);

    internal static ITaskbarList3 CreateTaskbarList()
    {
        var type = Type.GetTypeFromCLSID(CLSID_TaskbarList)!;
        var obj = (ITaskbarList3)Activator.CreateInstance(type)!;
        obj.HrInit();
        return obj;
    }
}

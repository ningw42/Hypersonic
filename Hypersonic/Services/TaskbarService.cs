using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

using Hypersonic.Contracts.Services;
using Hypersonic.Interop;

using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace Hypersonic.Services;

public sealed class TaskbarService : ITaskbarService
{
    private const int IconSize = 20;

    private readonly IPlaybackService _playbackService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger _log;

    private IntPtr _hwnd;
    private ITaskbarList3? _taskbarList;
    private WindowSubclass? _subclass;
    private uint _taskbarButtonCreatedMessage;

    // Thumbnail toolbar button icons
    private IntPtr _iconPreviousLight, _iconPreviousDark;
    private IntPtr _iconPlayLight, _iconPlayDark;
    private IntPtr _iconPauseLight, _iconPauseDark;
    private IntPtr _iconNextLight, _iconNextDark;

    private bool _isDarkTaskbar = true;
    private bool _buttonsAdded;
    private bool _disposed;

    public TaskbarService(IPlaybackService playbackService, ILoggingService logService)
    {
        _playbackService = playbackService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _log = logService.GetLogger(nameof(TaskbarService));
    }

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;

        try
        {
            _taskbarList = TaskbarNativeMethods.CreateTaskbarList();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to create taskbar COM object", exception: ex);
            return;
        }

        _taskbarButtonCreatedMessage = TaskbarNativeMethods.RegisterWindowMessageW("TaskbarButtonCreated");

        _isDarkTaskbar = DetectDarkTaskbar();
        _log.Info("Taskbar initialized", new { IsDark = _isDarkTaskbar });
        CreateAllIcons();

        // Set up the window subclass BEFORE adding buttons.
        // ThumbBarAddButtons must be deferred until the TaskbarButtonCreated message is received.
        _subclass = new WindowSubclass(_hwnd, OnWindowMessage);

        _playbackService.TrackChanged += OnTrackChanged;
        _playbackService.PlaybackStateChanged += OnPlaybackStateChanged;
    }

    public void OnSystemThemeChanged()
    {
        var wasDark = _isDarkTaskbar;
        _isDarkTaskbar = DetectDarkTaskbar();
        if (wasDark != _isDarkTaskbar)
        {
            DestroyAllIcons();
            CreateAllIcons();
            UpdateButtons();
        }
    }

    private (bool handled, IntPtr result) OnWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Wait for this message before calling any ITaskbarList3 methods (per Microsoft docs).
        if (msg == _taskbarButtonCreatedMessage && _taskbarButtonCreatedMessage != 0)
        {
            _buttonsAdded = false;
            AddThumbBarButtons();
            return (false, IntPtr.Zero);
        }

        if (msg == TaskbarNativeMethods.WM_COMMAND)
        {
            var buttonId = (uint)(wParam.ToInt64() & 0xFFFF);
            if (buttonId is TaskbarNativeMethods.BUTTON_PREVIOUS or TaskbarNativeMethods.BUTTON_PLAYPAUSE or TaskbarNativeMethods.BUTTON_NEXT)
            {
                _dispatcherQueue.TryEnqueue(() => HandleButtonClick(buttonId));
                return (true, IntPtr.Zero);
            }
        }

        return (false, IntPtr.Zero);
    }

    private void HandleButtonClick(uint buttonId)
    {
        switch (buttonId)
        {
            case TaskbarNativeMethods.BUTTON_PREVIOUS:
                _playbackService.PreviousTrack();
                break;
            case TaskbarNativeMethods.BUTTON_PLAYPAUSE:
                if (_playbackService.IsPlaying)
                {
                    _playbackService.Pause();
                }
                else
                {
                    _playbackService.Resume();
                }
                break;
            case TaskbarNativeMethods.BUTTON_NEXT:
                _playbackService.NextTrack();
                break;
        }
    }

    private void AddThumbBarButtons()
    {
        if (_buttonsAdded || _taskbarList == null)
        {
            return;
        }

        var buttons = CreateButtonArray();
        var hr = _taskbarList.ThumbBarAddButtons(_hwnd, 3, buttons);
        if (hr >= 0)
        {
            _buttonsAdded = true;
        }
    }

    private void UpdateButtons()
    {
        if (!_buttonsAdded || _taskbarList == null)
        {
            return;
        }

        var buttons = CreateButtonArray();
        _taskbarList.ThumbBarUpdateButtons(_hwnd, 3, buttons);
    }

    private THUMBBUTTON[] CreateButtonArray()
    {
        var isPlaying = _playbackService.IsPlaying;
        var hasTrack = _playbackService.NowPlaying != null;

        return
        [
            new THUMBBUTTON
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = TaskbarNativeMethods.BUTTON_PREVIOUS,
                hIcon = _isDarkTaskbar ? _iconPreviousLight : _iconPreviousDark,
                szTip = "Previous",
                dwFlags = _playbackService.HasPrevious ? THUMBBUTTONFLAGS.THBF_ENABLED : THUMBBUTTONFLAGS.THBF_DISABLED,
            },
            new THUMBBUTTON
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = TaskbarNativeMethods.BUTTON_PLAYPAUSE,
                hIcon = isPlaying
                    ? (_isDarkTaskbar ? _iconPauseLight : _iconPauseDark)
                    : (_isDarkTaskbar ? _iconPlayLight : _iconPlayDark),
                szTip = isPlaying ? "Pause" : "Play",
                dwFlags = hasTrack ? THUMBBUTTONFLAGS.THBF_ENABLED : THUMBBUTTONFLAGS.THBF_DISABLED,
            },
            new THUMBBUTTON
            {
                dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
                iId = TaskbarNativeMethods.BUTTON_NEXT,
                hIcon = _isDarkTaskbar ? _iconNextLight : _iconNextDark,
                szTip = "Next",
                dwFlags = _playbackService.HasNext ? THUMBBUTTONFLAGS.THBF_ENABLED : THUMBBUTTONFLAGS.THBF_DISABLED,
            },
        ];
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        _dispatcherQueue.TryEnqueue(UpdateButtons);
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        _dispatcherQueue.TryEnqueue(UpdateButtons);
    }

    // --- Theme Detection ---

    private static bool DetectDarkTaskbar()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 1);
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return true; // Default to dark taskbar
        }
    }

    // --- Icon Creation from Segoe Fluent Icons ---

    private void CreateAllIcons()
    {
        // Segoe Fluent Icons / Segoe MDL2 Assets glyph codes
        const string previousGlyph = "\uE892"; // Previous
        const string playGlyph = "\uE768";     // Play
        const string pauseGlyph = "\uE769";    // Pause
        const string nextGlyph = "\uE893";     // Next

        _iconPreviousLight = CreateIconFromGlyph(previousGlyph, Color.White);
        _iconPreviousDark = CreateIconFromGlyph(previousGlyph, Color.Black);
        _iconPlayLight = CreateIconFromGlyph(playGlyph, Color.White);
        _iconPlayDark = CreateIconFromGlyph(playGlyph, Color.Black);
        _iconPauseLight = CreateIconFromGlyph(pauseGlyph, Color.White);
        _iconPauseDark = CreateIconFromGlyph(pauseGlyph, Color.Black);
        _iconNextLight = CreateIconFromGlyph(nextGlyph, Color.White);
        _iconNextDark = CreateIconFromGlyph(nextGlyph, Color.Black);
    }

    private static IntPtr CreateIconFromGlyph(string glyph, Color color)
    {
        using var bitmap = new Bitmap(IconSize, IconSize);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using var font = new Font("Segoe MDL2 Assets", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);

            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            g.DrawString(glyph, font, brush, new RectangleF(0, 0, IconSize, IconSize), sf);
        }

        return bitmap.GetHicon();
    }

    private void DestroyAllIcons()
    {
        DestroyIconSafe(ref _iconPreviousLight);
        DestroyIconSafe(ref _iconPreviousDark);
        DestroyIconSafe(ref _iconPlayLight);
        DestroyIconSafe(ref _iconPlayDark);
        DestroyIconSafe(ref _iconPauseLight);
        DestroyIconSafe(ref _iconPauseDark);
        DestroyIconSafe(ref _iconNextLight);
        DestroyIconSafe(ref _iconNextDark);
    }

    private static void DestroyIconSafe(ref IntPtr icon)
    {
        if (icon != IntPtr.Zero)
        {
            TaskbarNativeMethods.DestroyIcon(icon);
            icon = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _playbackService.TrackChanged -= OnTrackChanged;
        _playbackService.PlaybackStateChanged -= OnPlaybackStateChanged;

        _subclass?.Dispose();

        DestroyAllIcons();

        if (_taskbarList != null)
        {
            Marshal.ReleaseComObject(_taskbarList);
            _taskbarList = null;
        }
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;

namespace Dabom;

public static class WindowChromeSupport
{
    private const int WmNcHitTest = 0x0084;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcMouseLeave = 0x02A2;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcLeftButtonUp = 0x00A2;
    private const int WmCancelMode = 0x001F;
    private const int WmCaptureChanged = 0x0215;
    private const int HtMaxButton = 9;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const double CustomCaptionHeight = 40;
    private const uint MonitorDefaultToNearest = 2;
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;
    private const uint WsOverlappedWindow = 0x00CF0000;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(WindowChromeSupport),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static readonly DependencyPropertyKey
        IsCaptionHoveredPropertyKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "IsCaptionHovered",
                typeof(bool),
                typeof(WindowChromeSupport),
                new PropertyMetadata(false));

    public static readonly DependencyProperty IsCaptionHoveredProperty =
        IsCaptionHoveredPropertyKey.DependencyProperty;

    public static bool GetIsCaptionHovered(DependencyObject element) =>
        (bool)element.GetValue(IsCaptionHoveredProperty);

    private static void SetIsCaptionHovered(
        DependencyObject element,
        bool value) =>
        element.SetValue(IsCaptionHoveredPropertyKey, value);

    private static readonly DependencyPropertyKey
        IsCaptionPressedPropertyKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "IsCaptionPressed",
                typeof(bool),
                typeof(WindowChromeSupport),
                new PropertyMetadata(false));

    public static readonly DependencyProperty IsCaptionPressedProperty =
        IsCaptionPressedPropertyKey.DependencyProperty;

    public static bool GetIsCaptionPressed(DependencyObject element) =>
        (bool)element.GetValue(IsCaptionPressedProperty);

    private static void SetIsCaptionPressed(
        DependencyObject element,
        bool value) =>
        element.SetValue(IsCaptionPressedPropertyKey, value);

    private static readonly DependencyPropertyKey
        LegacyContentMarginPropertyKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "LegacyContentMargin",
                typeof(Thickness),
                typeof(WindowChromeSupport),
                new PropertyMetadata(new Thickness()));

    public static readonly DependencyProperty LegacyContentMarginProperty =
        LegacyContentMarginPropertyKey.DependencyProperty;

    public static Thickness GetLegacyContentMargin(
        DependencyObject element) =>
        (Thickness)element.GetValue(LegacyContentMarginProperty);

    private static void SetLegacyContentMargin(
        DependencyObject element,
        Thickness value) =>
        element.SetValue(LegacyContentMarginPropertyKey, value);

    private static void OnIsEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is Window window && (bool)e.NewValue)
        {
            InstallCommandBindings(window);
            window.SourceInitialized += OnSourceInitialized;
        }
    }

    private static void InstallCommandBindings(Window window)
    {
        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.MinimizeWindowCommand,
            (_, args) =>
            {
                SystemCommands.MinimizeWindow(window);
                args.Handled = true;
            },
            (_, args) =>
            {
                args.CanExecute = window.ResizeMode != ResizeMode.NoResize;
                args.Handled = true;
            }));
        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.MaximizeWindowCommand,
            (_, args) =>
            {
                SystemCommands.MaximizeWindow(window);
                args.Handled = true;
            },
            (_, args) =>
            {
                args.CanExecute =
                    window.WindowState != WindowState.Maximized
                    && window.ResizeMode
                        is ResizeMode.CanResize
                            or ResizeMode.CanResizeWithGrip;
                args.Handled = true;
            }));
        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.RestoreWindowCommand,
            (_, args) =>
            {
                SystemCommands.RestoreWindow(window);
                args.Handled = true;
            },
            (_, args) =>
            {
                args.CanExecute = window.WindowState != WindowState.Normal;
                args.Handled = true;
            }));
        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand,
            (_, args) =>
            {
                SystemCommands.CloseWindow(window);
                args.Handled = true;
            },
            (_, args) =>
            {
                args.CanExecute = true;
                args.Handled = true;
            }));
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        var window = (Window)sender!;
        window.SourceInitialized -= OnSourceInitialized;
        var handle = new WindowInteropHelper(window).Handle;
        PreserveClientArea(window, handle);
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => InstallHook(window, handle)));
    }

    private static void InstallHook(Window window, IntPtr handle)
    {
        if (HwndSource.FromHwnd(handle) is HwndSource source)
        {
            HwndSourceHook hook = (
                IntPtr hwnd,
                int message,
                IntPtr wParam,
                IntPtr lParam,
                ref bool handled) =>
                HandleWindowMessage(
                    window,
                    hwnd,
                    message,
                    wParam,
                    lParam,
                    ref handled);
            source.AddHook(hook);
            window.Closed += (_, _) => source.RemoveHook(hook);
        }
    }

    private static void PreserveClientArea(Window window, IntPtr handle)
    {
        var dpi = GetDpiForWindow(handle);
        var frame = new NativeRect();
        if (dpi == 0
            || !AdjustWindowRectExForDpi(
                ref frame,
                WsOverlappedWindow,
                false,
                0,
                dpi))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var dipsPerPixel = 96d / dpi;
        SetLegacyContentMargin(
            window,
            new Thickness(
                -frame.Left * dipsPerPixel,
                0,
                frame.Right * dipsPerPixel,
                0));
        var legacyNonClientHeight =
            (frame.Bottom - frame.Top) * dipsPerPixel;
        var delta = CustomCaptionHeight - legacyNonClientHeight;
        if (!double.IsNaN(window.Height))
        {
            window.Height += delta;
        }
        if (window.MinHeight > 0)
        {
            window.MinHeight += delta;
        }
    }

    private static IntPtr HandleWindowMessage(
        Window window,
        IntPtr handle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            ConstrainMaximizedWindow(handle, lParam);
            handled = true;
            return IntPtr.Zero;
        }

        if (message == WmNcHitTest)
        {
            var resizeHit = HitTestResizeBorder(
                window,
                handle,
                lParam);
            if (resizeHit != IntPtr.Zero)
            {
                handled = true;
                return resizeHit;
            }
        }

        if (message is WmNcMouseMove
            or WmNcMouseLeave
            or WmNcLeftButtonDown
            or WmNcLeftButtonUp
            or WmCancelMode
            or WmCaptureChanged)
        {
            return HandleMaximizeButtonMouse(
                window,
                handle,
                message,
                wParam,
                lParam,
                ref handled);
        }

        return HitTestMaximizeButton(
            window,
            message,
            lParam,
            ref handled);
    }

    private static IntPtr HitTestResizeBorder(
        Window window,
        IntPtr handle,
        IntPtr lParam)
    {
        if (window.WindowState != WindowState.Normal
            || window.ResizeMode
                is not (ResizeMode.CanResize
                    or ResizeMode.CanResizeWithGrip)
            || WindowChrome.GetWindowChrome(window) is not { } chrome)
        {
            return IntPtr.Zero;
        }

        var dpi = GetDpiForWindow(handle);
        if (dpi == 0 || !GetWindowRect(handle, out var bounds))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var scale = dpi / 96d;
        var point = UnpackScreenPoint(lParam);
        var onLeft = point.X
            < bounds.Left
                + Math.Ceiling(chrome.ResizeBorderThickness.Left * scale);
        var onRight = point.X
            >= bounds.Right
                - Math.Ceiling(chrome.ResizeBorderThickness.Right * scale);
        var onTop = point.Y
            < bounds.Top
                + Math.Ceiling(chrome.ResizeBorderThickness.Top * scale);
        var onBottom = point.Y
            >= bounds.Bottom
                - Math.Ceiling(chrome.ResizeBorderThickness.Bottom * scale);

        if (onTop && onLeft)
        {
            return new IntPtr(HtTopLeft);
        }
        if (onTop && onRight)
        {
            return new IntPtr(HtTopRight);
        }
        if (onBottom && onLeft)
        {
            return new IntPtr(HtBottomLeft);
        }
        if (onBottom && onRight)
        {
            return new IntPtr(HtBottomRight);
        }
        if (onLeft)
        {
            return new IntPtr(HtLeft);
        }
        if (onRight)
        {
            return new IntPtr(HtRight);
        }
        if (onTop)
        {
            return new IntPtr(HtTop);
        }
        if (onBottom)
        {
            return new IntPtr(HtBottom);
        }

        return IntPtr.Zero;
    }

    private static IntPtr HandleMaximizeButtonMouse(
        Window window,
        IntPtr handle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        var button = GetMaximizeButton(window);
        if (button is null)
        {
            return IntPtr.Zero;
        }

        if (message == WmNcMouseMove)
        {
            var isOver = wParam.ToInt32() == HtMaxButton
                && IsPointOverButton(button, lParam);
            SetIsCaptionHovered(button, isOver);
            if (isOver)
            {
                var tracking = new TrackMouseEventInfo
                {
                    Size = Marshal.SizeOf<TrackMouseEventInfo>(),
                    Flags = TmeLeave | TmeNonClient,
                    TrackWindowHandle = handle
                };
                TrackMouseEvent(ref tracking);
            }
            return IntPtr.Zero;
        }

        if (message == WmNcLeftButtonDown)
        {
            var isPressed = wParam.ToInt32() == HtMaxButton
                && IsPointOverButton(button, lParam);
            SetIsCaptionHovered(button, isPressed);
            SetIsCaptionPressed(button, isPressed);
            return IntPtr.Zero;
        }

        if (message == WmNcLeftButtonUp)
        {
            var invoke = GetIsCaptionPressed(button)
                && wParam.ToInt32() == HtMaxButton
                && IsPointOverButton(button, lParam);
            SetIsCaptionPressed(button, false);
            SetIsCaptionHovered(button, invoke);
            if (invoke)
            {
                handled = true;
                if (window.WindowState == WindowState.Maximized)
                {
                    SystemCommands.RestoreWindow(window);
                }
                else
                {
                    SystemCommands.MaximizeWindow(window);
                }
            }
            return IntPtr.Zero;
        }

        SetIsCaptionHovered(button, false);
        SetIsCaptionPressed(button, false);
        return IntPtr.Zero;
    }

    private static void ConstrainMaximizedWindow(
        IntPtr handle,
        IntPtr lParam)
    {
        var monitor = MonitorFromWindow(
            handle,
            MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (monitor == IntPtr.Zero
            || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.MaxPosition.X =
            monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left;
        minMaxInfo.MaxPosition.Y =
            monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top;
        minMaxInfo.MaxSize.X =
            monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
        minMaxInfo.MaxSize.Y =
            monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, false);
    }

    private static IntPtr HitTestMaximizeButton(
        Window window,
        int message,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmNcHitTest
            || window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
        {
            return IntPtr.Zero;
        }

        var button = GetMaximizeButton(window);
        if (button is null || !IsPointOverButton(button, lParam))
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(HtMaxButton);
    }

    private static Button? GetMaximizeButton(Window window)
    {
        window.ApplyTemplate();
        return window.Template.FindName(
            "PART_MaximizeRestoreButton",
            window) is Button { IsVisible: true, IsEnabled: true } button
                ? button
                : null;
    }

    private static bool IsPointOverButton(
        Button button,
        IntPtr lParam)
    {
        var nativePoint = UnpackScreenPoint(lParam);
        var screenPoint = new Point(nativePoint.X, nativePoint.Y);
        var buttonPoint = button.PointFromScreen(screenPoint);
        if (buttonPoint.X < 0
            || buttonPoint.Y < 0
            || buttonPoint.X >= button.ActualWidth
            || buttonPoint.Y >= button.ActualHeight)
        {
            return false;
        }

        return true;
    }

    private static NativePoint UnpackScreenPoint(IntPtr lParam)
    {
        var packedPoint = lParam.ToInt64();
        return new NativePoint
        {
            X = unchecked((short)(packedPoint & 0xffff)),
            Y = unchecked((short)((packedPoint >> 16) & 0xffff))
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr TrackWindowHandle;
        public uint HoverTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(
        ref NativeRect rect,
        uint style,
        bool hasMenu,
        uint extendedStyle,
        uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr windowHandle,
        uint flags);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetMonitorInfoW",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitorHandle,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(
        ref TrackMouseEventInfo eventTrack);
}

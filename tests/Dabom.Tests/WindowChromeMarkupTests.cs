using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Dabom.Tests;

[TestClass]
public sealed class WindowChromeMarkupTests
{
    [TestMethod]
    public void VerticalScrollBar_UsesCompactSharedTemplateWithoutChangingHorizontal()
    {
        var theme = ReadThemeMarkup();

        StringAssert.Contains(theme, "<Style x:Key=\"ScrollBarPageButtonStyle\"");
        StringAssert.Contains(theme, "<Style x:Key=\"VerticalScrollThumbStyle\"");
        StringAssert.Contains(theme, "<Trigger Property=\"Orientation\" Value=\"Vertical\">");
        StringAssert.Contains(theme, "<Setter Property=\"Width\" Value=\"8\" />");
        StringAssert.Contains(theme, "Width=\"6\" MinHeight=\"32\"");
        StringAssert.Contains(theme, "CornerRadius=\"3\"");
        StringAssert.Contains(theme, "Background=\"{StaticResource MutedBrush}\"");
        StringAssert.Contains(theme, "Property=\"IsMouseOver\" Value=\"True\"");
        StringAssert.Contains(theme, "Property=\"IsDragging\" Value=\"True\"");
        StringAssert.Contains(theme, "Command=\"{x:Static ScrollBar.PageUpCommand}\"");
        StringAssert.Contains(theme, "Command=\"{x:Static ScrollBar.PageDownCommand}\"");
        Assert.IsFalse(theme.Contains("ScrollBar.LineUpCommand", StringComparison.Ordinal));
        Assert.IsFalse(theme.Contains("ScrollBar.LineDownCommand", StringComparison.Ordinal));
        Assert.IsFalse(
            theme.Contains(
                "<Trigger Property=\"Orientation\" Value=\"Horizontal\">",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void SharedWindowChrome_UsesRequiredTitleBarAndCommands()
    {
        var theme = ReadThemeMarkup();

        StringAssert.Contains(theme, "<SolidColorBrush x:Key=\"CloseHoverBrush\" Color=\"#FF8B1E2D\" />");
        StringAssert.Contains(theme, "CaptionHeight=\"40\"");
        StringAssert.Contains(theme, "GlassFrameThickness=\"0\"");
        StringAssert.Contains(theme, "UseAeroCaptionButtons=\"False\"");
        StringAssert.Contains(theme, "Height=\"40\"");
        StringAssert.Contains(theme, "Width=\"16\" Height=\"16\"");
        StringAssert.Contains(theme, "Background=\"{StaticResource PageBrush}\"");
        StringAssert.Contains(theme, "BorderBrush=\"{StaticResource LineBrush}\"");
        StringAssert.Contains(theme, "x:Name=\"PART_MinimizeButton\"");
        StringAssert.Contains(theme, "x:Name=\"PART_MaximizeRestoreButton\"");
        StringAssert.Contains(theme, "x:Name=\"PART_CloseButton\"");
        StringAssert.Contains(theme, "SystemCommands.MinimizeWindowCommand");
        StringAssert.Contains(theme, "SystemCommands.MaximizeWindowCommand");
        StringAssert.Contains(theme, "SystemCommands.RestoreWindowCommand");
        StringAssert.Contains(theme, "SystemCommands.CloseWindowCommand");
        StringAssert.Contains(theme, "AutomationProperties.Name=\"최소화\"");
        StringAssert.Contains(theme, "Property=\"AutomationProperties.Name\"");
        StringAssert.Contains(theme, "Value=\"최대화\"");
        StringAssert.Contains(theme, "Value=\"복원\"");
        StringAssert.Contains(theme, "AutomationProperties.Name=\"닫기\"");
        StringAssert.Contains(theme, "Property=\"ResizeMode\" Value=\"NoResize\"");
        StringAssert.Contains(theme, "Binding=\"{Binding WindowState");
        StringAssert.Contains(theme, "Value=\"Maximized\"");
    }

    [TestMethod]
    public void WindowChromeSupport_ExposesNativeContractsAndPreservesClientArea()
    {
        var code = ReadProjectFile("WindowChromeSupport.cs");
        var theme = ReadThemeMarkup();

        StringAssert.Contains(code, "private const int WmNcHitTest = 0x0084;");
        StringAssert.Contains(code, "private const int WmNcMouseMove = 0x00A0;");
        StringAssert.Contains(code, "private const int WmNcMouseLeave = 0x02A2;");
        StringAssert.Contains(code, "private const int WmNcLeftButtonDown = 0x00A1;");
        StringAssert.Contains(code, "private const int WmNcLeftButtonUp = 0x00A2;");
        StringAssert.Contains(code, "private const int HtMaxButton = 9;");
        StringAssert.Contains(code, "private const int HtRight = 11;");
        StringAssert.Contains(code, "private const int HtTop = 12;");
        StringAssert.Contains(code, "private const int HtTopRight = 14;");
        StringAssert.Contains(code, "source.AddHook");
        StringAssert.Contains(code, "DispatcherPriority.Loaded");
        StringAssert.Contains(code, "PART_MaximizeRestoreButton");
        StringAssert.Contains(code, "button.PointFromScreen");
        StringAssert.Contains(code, "handled = true;");
        StringAssert.Contains(code, "return new IntPtr(HtMaxButton);");
        StringAssert.Contains(code, "AdjustWindowRectExForDpi");
        StringAssert.Contains(code, "GetDpiForWindow");
        StringAssert.Contains(code, "CustomCaptionHeight - legacyNonClientHeight");
        StringAssert.Contains(code, "private const int WmGetMinMaxInfo = 0x0024;");
        StringAssert.Contains(code, "MonitorFromWindow");
        StringAssert.Contains(code, "GetMonitorInfo");
        StringAssert.Contains(code, "GetWindowRect");
        StringAssert.Contains(code, "HitTestResizeBorder");
        StringAssert.Contains(code, "MaxPosition");
        StringAssert.Contains(code, "MaxSize");
        StringAssert.Contains(code, "IsCaptionHoveredProperty");
        StringAssert.Contains(code, "IsCaptionPressedProperty");
        StringAssert.Contains(code, "LegacyContentMarginProperty");
        StringAssert.Contains(code, "SetLegacyContentMargin");
        StringAssert.Contains(code, "TrackMouseEvent");
        StringAssert.Contains(code, "handled = isPressed;");
        StringAssert.Contains(code, "SystemCommands.MaximizeWindow(window)");
        StringAssert.Contains(code, "SystemCommands.RestoreWindow(window)");
        StringAssert.Contains(theme, "Property=\"local:WindowChromeSupport.IsCaptionHovered\"");
        StringAssert.Contains(theme, "Property=\"local:WindowChromeSupport.IsCaptionPressed\"");
        StringAssert.Contains(theme, "Path=(local:WindowChromeSupport.LegacyContentMargin)");
        StringAssert.Contains(theme, "TargetName=\"WindowContentPresenter\"");
        StringAssert.Contains(theme, "Property=\"Margin\" Value=\"0\"");
        Assert.IsFalse(code.Contains("SystemParameters.CaptionHeight", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TargetWindows_ProvideSharedChromeInputsWithoutLocalDuplication()
    {
        var main = ReadProjectFile("MainWindow.xaml");
        var metadata = ReadProjectFile("MetadataWindow.xaml");
        var about = ReadProjectFile("AboutWindow.xaml");

        foreach (var markup in new[] { main, metadata, about })
        {
            StringAssert.Contains(markup, "Style=\"{StaticResource {x:Type Window}}\"");
            StringAssert.Contains(markup, "Title=");
            StringAssert.Contains(markup, "Icon=\"Assets/Dabom.ico\"");
            Assert.IsFalse(markup.Contains("PART_MaximizeRestoreButton", StringComparison.Ordinal));
        }

        StringAssert.Contains(about, "ResizeMode=\"NoResize\"");
    }

    [STATestMethod]
    public void SharedWindowChrome_RuntimeContractsHold()
    {
        var theme = (ResourceDictionary)Application.LoadComponent(
            new Uri(
                "/Dabom;component/Styles/DabomTheme.xaml",
                UriKind.Relative));
        var legacyContent = new Border();
        var legacyWindow = new Window
        {
            Content = legacyContent,
            Width = 800,
            Height = 600,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        var customContent = new Border();
        var window = new Window
        {
            Content = customContent,
            Style = (Style)theme[typeof(Window)],
            Width = 800,
            Height = 600,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        var noResizeWindow = new Window
        {
            Style = (Style)theme[typeof(Window)],
            ResizeMode = ResizeMode.NoResize,
            Width = 560,
            Height = 520,
            ShowActivated = false,
            ShowInTaskbar = false
        };

        legacyWindow.Show();
        window.Show();
        noResizeWindow.Show();
        try
        {
            window.Dispatcher.Invoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => { }));
            window.ApplyTemplate();
            var button = window.Template.FindName(
                "PART_MaximizeRestoreButton",
                window) as Button;
            var closeButton = window.Template.FindName(
                "PART_CloseButton",
                window) as Button;

            Assert.IsNotNull(button);
            Assert.IsNotNull(closeButton);
            Assert.IsTrue(button.IsEnabled);
            Assert.IsTrue(closeButton.IsEnabled);
            Assert.AreEqual(
                new IntPtr(9),
                SendHitTest(
                    window,
                    button.PointToScreen(
                        new Point(
                            button.ActualWidth / 2,
                            button.ActualHeight / 2))));
            Assert.AreEqual(
                new IntPtr(12),
                SendHitTest(
                    window,
                    button.PointToScreen(
                        new Point(button.ActualWidth / 2, 1))));
            Assert.AreEqual(
                new IntPtr(11),
                SendHitTest(
                    window,
                    closeButton.PointToScreen(
                        new Point(
                            closeButton.ActualWidth - 1,
                            closeButton.ActualHeight / 2))));
            Assert.AreEqual(
                new IntPtr(14),
                SendHitTest(
                    window,
                    closeButton.PointToScreen(
                        new Point(closeButton.ActualWidth - 1, 1))));
            Assert.AreEqual(
                legacyContent.ActualWidth,
                customContent.ActualWidth,
                0.5);
            Assert.IsFalse(
                IsResizeHit(SendHitTest(
                    noResizeWindow,
                    noResizeWindow.PointToScreen(
                        new Point(noResizeWindow.ActualWidth / 2, 1)))));
            Assert.IsFalse(
                IsResizeHit(SendHitTest(
                    noResizeWindow,
                    noResizeWindow.PointToScreen(
                        new Point(
                            noResizeWindow.ActualWidth - 1,
                            noResizeWindow.ActualHeight / 2)))));
        }
        finally
        {
            noResizeWindow.Close();
            window.Close();
            legacyWindow.Close();
        }
    }

    private static string ReadThemeMarkup() =>
        File.ReadAllText(Path.Combine(
            ProjectDirectory(), "Styles", "DabomTheme.xaml"));

    private static string ReadProjectFile(string relativePath) =>
        File.ReadAllText(Path.Combine(ProjectDirectory(), relativePath));

    private static string ProjectDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "Dabom"));

    private static IntPtr SendHitTest(Window window, Point screenPoint) =>
        SendMessage(
            new WindowInteropHelper(window).Handle,
            0x0084,
            IntPtr.Zero,
            PackScreenPoint(screenPoint));

    private static IntPtr PackScreenPoint(Point point)
    {
        var x = (int)Math.Round(point.X);
        var y = (int)Math.Round(point.Y);
        return new IntPtr(unchecked((y << 16) | (x & 0xffff)));
    }

    private static bool IsResizeHit(IntPtr hit) =>
        hit.ToInt32() is >= 10 and <= 17;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);
}

using Dabom.Library;
using Dabom.Main;
using Dabom.Metadata;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Dabom.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SearchResetTests
{
    [STATestMethod]
    public void MainSearch_ClearButtonAppearsForTextAndClearsIt()
    {
        EnsureApplicationResources();
        var root = Directory.CreateTempSubdirectory("dabom-search-reset-");
        var viewModel = CreateMainViewModel(root.FullName);
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            Pump(window);
            var clearButton = window.FindName("SearchClearButton") as Button;
            var shortcutHint = window.FindName("SearchShortcutHint") as Border;

            Assert.IsNotNull(clearButton);
            Assert.IsNotNull(shortcutHint);
            Assert.AreEqual("검색어 지우기", AutomationProperties.GetName(clearButton));
            Assert.AreEqual("검색어 지우기", clearButton.ToolTip);
            Assert.AreEqual(Visibility.Collapsed, clearButton.Visibility);
            Assert.AreEqual(Visibility.Visible, shortcutHint.Visibility);

            viewModel.SearchText = "기생충";
            Pump(window);

            Assert.AreEqual(Visibility.Visible, clearButton.Visibility);
            Assert.AreEqual(Visibility.Collapsed, shortcutHint.Visibility);

            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            Assert.AreEqual(string.Empty, viewModel.SearchText);
            Assert.AreEqual(Visibility.Collapsed, clearButton.Visibility);
            Assert.AreEqual(Visibility.Visible, shortcutHint.Visibility);
        }
        finally
        {
            window.Close();
            root.Delete(true);
        }
    }

    [STATestMethod]
    public void MainSearch_EscapeClearsOnlyWhenRaisedFromSearchBox()
    {
        EnsureApplicationResources();
        var root = Directory.CreateTempSubdirectory("dabom-search-escape-");
        var viewModel = CreateMainViewModel(root.FullName);
        var window = new MainWindow { DataContext = viewModel };
        using var inputSource = CreateInputSource("DabomSearchResetTest");
        try
        {
            Pump(window);
            var searchBox = window.FindName("SearchBox") as TextBox;
            var videoList = window.FindName("VideoList") as ListBox;
            Assert.IsNotNull(searchBox);
            Assert.IsNotNull(videoList);

            viewModel.SearchText = "지울 검색어";
            var fromSearch = RaiseEscape(
                searchBox,
                Keyboard.PreviewKeyDownEvent,
                inputSource);

            Assert.IsTrue(fromSearch.Handled);
            Assert.AreEqual(string.Empty, viewModel.SearchText);

            viewModel.SearchText = "유지할 검색어";
            RaiseEscape(videoList, Keyboard.PreviewKeyDownEvent, inputSource);

            Assert.AreEqual("유지할 검색어", viewModel.SearchText);
        }
        finally
        {
            window.Close();
            root.Delete(true);
        }
    }

    [STATestMethod]
    public void MetadataSearch_ClearButtonClosesResultsAndPreservesEdits()
    {
        EnsureApplicationResources();
        var viewModel = CreateMetadataEditor();
        var window = new MetadataWindow { DataContext = viewModel };
        try
        {
            Pump(window);
            var clearButton = window.FindName("SearchClearButton") as Button;
            Assert.IsNotNull(clearButton);
            Assert.AreEqual("검색어 지우기", AutomationProperties.GetName(clearButton));
            Assert.AreEqual("검색어 지우기", clearButton.ToolTip);
            Assert.AreEqual(Visibility.Visible, clearButton.Visibility);

            viewModel.Title = "사용자 편집 제목";
            viewModel.IsSearchPopupOpen = true;
            clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(window);

            Assert.AreEqual(string.Empty, viewModel.SearchText);
            Assert.IsFalse(viewModel.IsSearchPopupOpen);
            Assert.AreEqual("사용자 편집 제목", viewModel.Title);
            Assert.AreEqual(Visibility.Collapsed, clearButton.Visibility);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void MetadataSearch_EscapeClearsOnlyWhenRaisedFromSearchBox()
    {
        EnsureApplicationResources();
        var viewModel = CreateMetadataEditor();
        var window = new MetadataWindow { DataContext = viewModel };
        using var inputSource = CreateInputSource("DabomMetadataSearchResetTest");
        try
        {
            Pump(window);
            var searchBox = window.FindName("SearchBox") as TextBox;
            var titleBox = window.FindName("TitleBox") as TextBox;
            Assert.IsNotNull(searchBox);
            Assert.IsNotNull(titleBox);

            viewModel.IsSearchPopupOpen = true;
            var fromSearch = RaiseEscape(
                searchBox,
                Keyboard.KeyDownEvent,
                inputSource);

            Assert.IsTrue(fromSearch.Handled);
            Assert.AreEqual(string.Empty, viewModel.SearchText);
            Assert.IsFalse(viewModel.IsSearchPopupOpen);

            viewModel.SearchText = "유지할 검색어";
            RaiseEscape(titleBox, Keyboard.KeyDownEvent, inputSource);

            Assert.AreEqual("유지할 검색어", viewModel.SearchText);
        }
        finally
        {
            window.Close();
        }
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        if (application.TryFindResource("PageBrush") is not null)
        {
            return;
        }

        application.Resources.MergedDictionaries.Add(
            (ResourceDictionary)Application.LoadComponent(
                new Uri(
                    "/Dabom;component/Styles/DabomTheme.xaml",
                    UriKind.Relative)));
    }

    private static void Pump(DispatcherObject owner) =>
        owner.Dispatcher.Invoke(
            DispatcherPriority.DataBind,
            new Action(() => { }));

    private static KeyEventArgs RaiseEscape(
        UIElement target,
        RoutedEvent routedEvent,
        PresentationSource inputSource)
    {
        var args = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            inputSource,
            Environment.TickCount,
            Key.Escape)
        {
            RoutedEvent = routedEvent
        };
        target.RaiseEvent(args);
        return args;
    }

    private static HwndSource CreateInputSource(string name) => new(
        new HwndSourceParameters
        {
            WindowName = name,
            WindowStyle = 0,
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000
        });

    private static MainViewModel CreateMainViewModel(string root) => new(
        new LibraryStore(root),
        new EmptyScanner(),
        new LibraryData(),
        _ => true,
        () => DateTimeOffset.UtcNow,
        _ => 0);

    private static MetadataEditorViewModel CreateMetadataEditor() => new(
        @"C:\Movie.mkv",
        new VideoRecord { Title = "기생충" },
        null,
        (_, _) => Task.FromResult<string?>(null));

    private sealed class EmptyScanner : ILibraryScanner
    {
        public Task<ScanResult> ScanAsync(
            IReadOnlyList<string> locations,
            IReadOnlyDictionary<string, VideoRecord> existingFileCache,
            CancellationToken cancellationToken) =>
            Task.FromResult<ScanResult>(new(
                new Dictionary<string, ScannedVideo>(),
                []));
    }
}

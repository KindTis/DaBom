using Dabom.Library;
using Dabom.Main;
using Dabom.Metadata;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Dabom;

public partial class MainWindow : Window
{
    private const int ExtendedStyle = -20;
    private const long TransparentStyle = 0x20;
    private const long NoActivateStyle = 0x08000000;
    private const double LibraryToolbarBaseline = 18d;
    private const int DoubleClickWidthMetric = 36;
    private const int DoubleClickHeightMetric = 37;
    private const int MaxVisibleToasts = 5;
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ToastTransition = TimeSpan.FromMilliseconds(200);

    private ListBoxItem? _hoveredCard;
    private SeasonGroupKey? _seasonReturnKey;
    private double _seasonReturnOffset;
    private int? _seasonEntryClickTimestamp;
    private Point _seasonEntryClickPosition;
    private readonly Queue<ToastEntry> _pendingToasts = [];
    private readonly Dictionary<ToastEntry, FrameworkElement> _toastElements = [];
    private readonly CancellationTokenSource _toastCancellation = new();
    private readonly SemaphoreSlim _toastTransitionGate = new(1, 1);
    private long _nextToastId;
    private bool _toastPumpRunning;

    private sealed record ToastEntry(long Id, string Message);

    internal static int DoubleClickTime => unchecked((int)GetDoubleClickTime());
    internal static double DoubleClickWidth => GetSystemMetrics(DoubleClickWidthMetric);
    private static double DoubleClickHeight => GetSystemMetrics(DoubleClickHeightMetric);

    public MainWindow()
    {
        InitializeComponent();
        CardPopup.CustomPopupPlacementCallback = PlaceCardPopup;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldViewModel)
        {
            oldViewModel.MetadataEditRequested -= OnMetadataEditRequested;
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            oldViewModel.ToastRequested -= OnToastRequested;
        }
        if (e.NewValue is MainViewModel newViewModel)
        {
            newViewModel.MetadataEditRequested += OnMetadataEditRequested;
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            newViewModel.ToastRequested += OnToastRequested;
        }
        _seasonReturnKey = null;
    }

    private void OnToastRequested(object? sender, string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnToastRequested(sender, message));
            return;
        }
        if (_toastCancellation.IsCancellationRequested) return;

        _pendingToasts.Enqueue(new ToastEntry(++_nextToastId, message));
        _ = PumpToastsAsync();
    }

    private async Task PumpToastsAsync()
    {
        if (_toastPumpRunning) return;
        _toastPumpRunning = true;
        try
        {
            while (!_toastCancellation.IsCancellationRequested
                && _toastElements.Count < MaxVisibleToasts
                && _pendingToasts.TryDequeue(out var entry))
            {
                await ShowToastAsync(entry, _toastCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_toastCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _toastPumpRunning = false;
        }
    }

    private async Task ShowToastAsync(ToastEntry entry, CancellationToken token)
    {
        await _toastTransitionGate.WaitAsync(token);
        try
        {
            var existingElements = _toastElements.Values.ToArray();
            var element = CreateToastVisual(entry.Message);
            _toastElements.Add(entry, element);
            ToastHost.Children.Add(element);
            ToastHost.UpdateLayout();
            AnnounceToast(entry.Message);
            _ = ExpireToastAsync(entry, token);

            if (!SystemParameters.ClientAreaAnimation)
            {
                element.Opacity = 1;
                return;
            }

            var shift = element.ActualHeight + element.Margin.Top + element.Margin.Bottom;
            foreach (var existing in existingElements)
            {
                AnimateToastOffset(existing, shift);
            }
            AnimateToastOffset(element, shift);
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(
                0,
                1,
                new Duration(ToastTransition)));
            await Task.Delay(ToastTransition, token);
        }
        finally
        {
            _toastTransitionGate.Release();
        }
    }

    private static void AnimateToastOffset(FrameworkElement element, double shift)
    {
        var transform = (TranslateTransform)element.RenderTransform;
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(shift, 0, new Duration(ToastTransition)));
    }

    private async Task ExpireToastAsync(ToastEntry entry, CancellationToken token)
    {
        try
        {
            await ExpireToastCoreAsync(entry, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task ExpireToastCoreAsync(ToastEntry entry, CancellationToken token)
    {
        await Task.Delay(ToastLifetime, token);
        await _toastTransitionGate.WaitAsync(token);
        try
        {
            if (!_toastElements.TryGetValue(entry, out var element)) return;
            if (SystemParameters.ClientAreaAnimation)
            {
                element.BeginAnimation(OpacityProperty, new DoubleAnimation(
                    1,
                    0,
                    new Duration(ToastTransition)));
                await Task.Delay(ToastTransition, token);
            }
            ToastHost.Children.Remove(element);
            _toastElements.Remove(entry);
        }
        finally
        {
            _toastTransitionGate.Release();
        }
        await PumpToastsAsync();
    }

    private Border CreateToastVisual(string message) => new()
    {
        MaxWidth = 360,
        Margin = new Thickness(0, 0, 0, 8),
        Padding = new Thickness(18, 14, 18, 14),
        Background = (Brush)FindResource("SurfaceBrush"),
        BorderBrush = (Brush)FindResource("LineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = (CornerRadius)FindResource("ControlCornerRadius"),
        IsHitTestVisible = false,
        RenderTransform = new TranslateTransform(),
        Child = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextBrush")
        }
    };

    private void AnnounceToast(string message)
    {
        ToastAnnouncement.Text = message;
        var peer = UIElementAutomationPeer.CreatePeerForElement(ToastAnnouncement)
            ?? new FrameworkElementAutomationPeer(ToastAnnouncement);
        peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    protected override void OnClosed(EventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.MetadataEditRequested -= OnMetadataEditRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.ToastRequested -= OnToastRequested;
        }
        _toastCancellation.Cancel();
        _pendingToasts.Clear();
        _toastElements.Clear();
        ToastHost.Children.Clear();
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSeasonView)
            && sender is MainViewModel viewModel
            && !viewModel.IsSeasonView
            && _seasonReturnKey is not null)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () => RestoreSeasonReturn(viewModel));
        }
    }

    private void OnMetadataEditRequested(object? sender, MetadataEditorViewModel editor)
    {
        new MetadataWindow
        {
            Owner = this,
            DataContext = editor
        }.ShowDialog();
    }

    private void OnToggleLocations(object sender, RoutedEventArgs e)
    {
        WarningsPopup.IsOpen = false;
        LocationsPopup.IsOpen = !LocationsPopup.IsOpen;
    }

    private void OnToggleWarnings(object sender, RoutedEventArgs e)
    {
        LocationsPopup.IsOpen = false;
        WarningsPopup.IsOpen = !WarningsPopup.IsOpen;
    }

    private void OnAbout(object sender, RoutedEventArgs e) =>
        new AboutWindow { Owner = this }.ShowDialog();

    private async void OnAddLocation(object sender, RoutedEventArgs e)
    {
        var viewModel = (MainViewModel)DataContext;
        if (!viewModel.CanMutateLibrary) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "동영상 보관 위치 선택"
        };
        if (dialog.ShowDialog(this) == true)
        {
            LocationsPopup.IsOpen = false;
            await viewModel.AddLocationAsync(dialog.FolderName);
        }
    }

    private void OnRemoveLocation(object sender, RoutedEventArgs e)
    {
        var path = (string)((Button)sender).Tag;
        var command = ((MainViewModel)DataContext).RemoveLocationCommand;
        if (command.CanExecute(path)) command.Execute(path);
    }

    private async void OnVideoDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var entryTimestamp = _seasonEntryClickTimestamp;
        _seasonEntryClickTimestamp = null;
        if (entryTimestamp is { } timestamp
            && IsContinuationOfSeasonEntryClick(
                timestamp,
                _seasonEntryClickPosition,
                e.Timestamp,
                e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        if (sender is not ListBoxItem item
            || item.DataContext is not VideoItemViewModel video)
        {
            return;
        }
        var viewModel = (MainViewModel)DataContext;
        if (viewModel.CanMutateLibrary)
        {
            await viewModel.PlayAsync(video);
        }
        e.Handled = true;
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item
            && item.DataContext is SeasonItemViewModel season)
        {
            e.Handled = true;
            _seasonEntryClickTimestamp = e.Timestamp;
            _seasonEntryClickPosition = e.GetPosition(this);
            OpenSeason(season);
        }
    }

    private void OpenSeason(SeasonItemViewModel season)
    {
        var viewModel = (MainViewModel)DataContext;
        _seasonReturnKey = season.Key;
        _seasonReturnOffset = MainScrollViewer.VerticalOffset;
        _hoveredCard = null;
        RefreshCardPopup();
        if (viewModel.OpenSeason(season))
        {
            VideoList.Focus();
        }
    }

    private async void OnVideoListKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = (MainViewModel)DataContext;
        if (e.Key == Key.Delete)
        {
            e.Handled = true;
            if (viewModel.SelectedItem is SeasonItemViewModel)
            {
                viewModel.RequestSeasonDeletionGuidance();
                return;
            }

            var request = viewModel.PrepareVideoDeletion();
            if (request is null) return;
            var message = request.Status == VideoFileStatus.Present
                ? $"“{request.Video.FileName}” ({request.Video.Path}) 파일을 휴지통으로 이동하시겠습니까? 파일과 영상 목록에서 제거됩니다."
                : $"“{request.Video.FileName}” ({request.Video.Path}) 파일이 존재하지 않습니다. 영상 목록에서 제거하시겠습니까?";
            if (MessageBox.Show(
                    this,
                    message,
                    "영상 삭제",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel) == MessageBoxResult.OK)
            {
                await viewModel.DeleteVideoAsync(request);
            }
            return;
        }

        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (viewModel.SelectedItem is SeasonItemViewModel season)
        {
            OpenSeason(season);
        }
        else if (viewModel.SelectedVideo is not null && viewModel.CanMutateLibrary)
        {
            await viewModel.PlayAsync(viewModel.SelectedVideo);
        }
    }

    private void OnReturnToLibrary(object sender, RoutedEventArgs e) =>
        ((MainViewModel)DataContext).CloseSeason();

    private void OnClearSearch(object sender, RoutedEventArgs e) => ClearSearch();

    private void ClearSearch()
    {
        var viewModel = (MainViewModel)DataContext;
        viewModel.SearchText = string.Empty;
        SearchBox.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = (MainViewModel)DataContext;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (!FilterComboBox.IsDropDownOpen
            && ReferenceEquals(e.OriginalSource, FilterComboBox)
            && e.Key is Key.Enter or Key.Space)
        {
            FilterComboBox.IsDropDownOpen = true;
            e.Handled = true;
        }
        else if (FilterComboBox.IsDropDownOpen
            && e.Key is Key.Enter or Key.Escape)
        {
            return;
        }
        else if (e.Key == Key.Escape && viewModel.IsSeasonView)
        {
            viewModel.CloseSeason();
            e.Handled = true;
        }
        else if (e.Key == Key.F1)
        {
            if (viewModel.CanMutateLibrary && viewModel.SelectedVideo is null)
            {
                viewModel.NotifyMissingSelection();
            }
            else if (viewModel.CanMutateLibrary
                && viewModel.OpenMetadataCommand.CanExecute(null))
            {
                viewModel.OpenMetadataCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape
            && ReferenceEquals(e.OriginalSource, SearchBox)
            && !string.IsNullOrEmpty(viewModel.SearchText))
        {
            ClearSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            LocationsPopup.IsOpen = false;
            WarningsPopup.IsOpen = false;
            _hoveredCard = null;
            RefreshCardPopup();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnMainScrollChanged(object sender, ScrollChangedEventArgs e) =>
        UpdateLibraryToolbarPosition();

    private void OnMainScrollContentLayoutUpdated(object? sender, EventArgs e) =>
        UpdateLibraryToolbarPosition();

    private void UpdateLibraryToolbarPosition()
    {
        if (!MainScrollViewer.IsArrangeValid || !LibraryToolbar.IsArrangeValid)
        {
            LibraryToolbarTransform.Y = 0;
            return;
        }

        var renderedTop =
            LibraryToolbar.TranslatePoint(new Point(), MainScrollViewer).Y;
        var originalTop = renderedTop - LibraryToolbarTransform.Y;
        var translation = GetLibraryToolbarTranslation(
            originalTop,
            MainScrollViewer.ScrollableHeight);

        if (LibraryToolbarTransform.Y != translation)
        {
            LibraryToolbarTransform.Y = translation;
        }
    }

    internal static double GetLibraryToolbarTranslation(
        double originalTop,
        double scrollableHeight) =>
        scrollableHeight > 0 && originalTop < LibraryToolbarBaseline
            ? LibraryToolbarBaseline - originalTop
            : 0;

    private void RefreshCardPopup()
    {
        if (_hoveredCard is null)
        {
            CardPopup.IsOpen = false;
            return;
        }

        CardPopup.PlacementTarget = _hoveredCard;
        CardPopup.DataContext = _hoveredCard.DataContext;
        CardPopup.IsOpen = true;
        MakeCardPopupClickThrough();
    }

    private void MakeCardPopupClickThrough()
    {
        if (PresentationSource.FromVisual(CardPopup.Child) is not HwndSource source) return;
        var style = GetWindowLongPtr(source.Handle, ExtendedStyle).ToInt64();
        SetWindowLongPtr(
            source.Handle,
            ExtendedStyle,
            new IntPtr(style | TransparentStyle | NoActivateStyle));
    }

    private void OnRoundedClipSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border border || border.Child is not UIElement child) return;
        var radius = Math.Max(0, border.CornerRadius.TopLeft - border.BorderThickness.Left);
        child.Clip = new RectangleGeometry(new Rect(child.RenderSize), radius, radius);
    }

    private void OnCardEnter(object sender, MouseEventArgs e)
    {
        var card = (ListBoxItem)sender;
        _hoveredCard = card;
        UpdateCardPopupPointerPlacement(card, e);
        RefreshCardPopup();
    }

    private void OnCardMove(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(_hoveredCard, sender))
        {
            UpdateCardPopupPointerPlacement((ListBoxItem)sender, e);
        }
    }

    private void UpdateCardPopupPointerPlacement(ListBoxItem card, MouseEventArgs e)
    {
        var position = e.GetPosition(card);
        CardPopup.Placement = PlacementMode.Custom;
        CardPopup.PlacementRectangle = new Rect(position.X, position.Y, 0, 0);
        CardPopup.HorizontalOffset = 0;
        CardPopup.VerticalOffset = 0;
    }

    private static CustomPopupPlacement[] PlaceCardPopup(
        Size popupSize,
        Size targetSize,
        Point offset) =>
        GetCardPopupPlacements(popupSize);

    internal static CustomPopupPlacement[] GetCardPopupPlacements(Size popupSize) =>
    [
        new(new Point(24, -76), PopupPrimaryAxis.Vertical),
        new(new Point(24, -popupSize.Height - 8), PopupPrimaryAxis.Vertical),
        new(new Point(-popupSize.Width - 24, -76), PopupPrimaryAxis.Vertical),
        new(new Point(-popupSize.Width - 24, -popupSize.Height - 8), PopupPrimaryAxis.Vertical),
    ];

    private void OnCardLeave(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(_hoveredCard, sender)) _hoveredCard = null;
        RefreshCardPopup();
    }

    private void RestoreSeasonReturn(MainViewModel viewModel)
    {
        var key = _seasonReturnKey;
        _seasonReturnKey = null;
        if (key is null)
        {
            VideoList.Focus();
            return;
        }

        var season = viewModel.FindSeason(key);
        if (season is null)
        {
            VideoList.Focus();
            return;
        }

        VideoList.UpdateLayout();
        MainScrollViewer.ScrollToVerticalOffset(_seasonReturnOffset);
        if (VideoList.ItemContainerGenerator.ContainerFromItem(season)
            is not ListBoxItem container)
        {
            VideoList.Focus();
            return;
        }

        var top = container.TranslatePoint(new Point(), MainScrollViewer).Y;
        if (!IntersectsViewport(
                top,
                container.ActualHeight,
                MainScrollViewer.ViewportHeight))
        {
            container.BringIntoView();
        }
        container.Focus();
    }

    internal static bool IntersectsViewport(
        double top,
        double height,
        double viewportHeight) =>
        top < viewportHeight && top + height > 0;

    internal static bool IsContinuationOfSeasonEntryClick(
        int entryTimestamp,
        Point entryPosition,
        int clickTimestamp,
        Point clickPosition)
    {
        var elapsed = unchecked(clickTimestamp - entryTimestamp);
        return elapsed >= 0
            && elapsed <= DoubleClickTime
            && Math.Abs(clickPosition.X - entryPosition.X) <= DoubleClickWidth
            && Math.Abs(clickPosition.Y - entryPosition.Y) <= DoubleClickHeight;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DustCanvas.Children.Count > 0) return;

        var random = new Random(1707);
        var brush = new SolidColorBrush(Color.FromArgb(45, 203, 201, 194));
        brush.Freeze();
        var width = Math.Max(DustCanvas.ActualWidth, 760);
        var height = Math.Max(DustCanvas.ActualHeight, 640);

        for (var index = 0; index < 24; index++)
        {
            var size = 1d + random.NextDouble() * 2d;
            var dust = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = brush,
                Opacity = 0.12 + random.NextDouble() * 0.18
            };
            Canvas.SetLeft(dust, random.NextDouble() * width);
            Canvas.SetTop(dust, random.NextDouble() * height);
            DustCanvas.Children.Add(dust);

            if (!SystemParameters.ClientAreaAnimation) continue;

            var duration = TimeSpan.FromSeconds(7 + random.NextDouble() * 7);
            dust.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                From = dust.Opacity,
                To = Math.Min(dust.Opacity + 0.22, 0.5),
                Duration = duration,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
            dust.BeginAnimation(MarginProperty, new ThicknessAnimation
            {
                From = new Thickness(0),
                To = new Thickness(
                    random.NextDouble() * 18 - 9,
                    random.NextDouble() * 34 - 17,
                    0,
                    0),
                Duration = duration,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

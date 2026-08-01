using Dabom.Main;
using Dabom.Metadata;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Dabom;

public partial class MainWindow : Window
{
    private const int ExtendedStyle = -20;
    private const long TransparentStyle = 0x20;
    private const long NoActivateStyle = 0x08000000;
    private const double LibraryToolbarBaseline = 18d;

    private ListBoxItem? _hoveredCard;
    private ListBoxItem? _focusedCard;

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
        }
        if (e.NewValue is MainViewModel newViewModel)
        {
            newViewModel.MetadataEditRequested += OnMetadataEditRequested;
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
        var viewModel = (MainViewModel)DataContext;
        if (viewModel.CanMutateLibrary)
        {
            await viewModel.PlayAsync(
                (VideoItemViewModel)((ListBoxItem)sender).DataContext);
        }
        e.Handled = true;
    }

    private async void OnVideoListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var viewModel = (MainViewModel)DataContext;
        if (viewModel.SelectedVideo is not null && viewModel.CanMutateLibrary)
        {
            await viewModel.PlayAsync(viewModel.SelectedVideo);
        }
    }

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
            _focusedCard = null;
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
        var activeCard = _hoveredCard ?? _focusedCard;
        if (activeCard is null)
        {
            CardPopup.IsOpen = false;
            return;
        }

        CardPopup.PlacementTarget = activeCard;
        if (_hoveredCard is null)
        {
            CardPopup.Placement = PlacementMode.Right;
            CardPopup.PlacementRectangle = Rect.Empty;
            CardPopup.HorizontalOffset = 12;
            CardPopup.VerticalOffset = 0;
        }
        CardPopup.DataContext = activeCard.DataContext;
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

    private void OnCardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _focusedCard = (ListBoxItem)sender;
        RefreshCardPopup();
    }

    private void OnCardLeave(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(_hoveredCard, sender)) _hoveredCard = null;
        RefreshCardPopup();
    }

    private void OnCardBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(_focusedCard, sender)) _focusedCard = null;
        RefreshCardPopup();
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
}

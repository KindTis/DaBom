using Dabom.Main;
using Dabom.Metadata;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Dabom;

public partial class MainWindow : Window
{
    private ListBoxItem? _hoveredCard;
    private ListBoxItem? _focusedCard;

    public MainWindow()
    {
        InitializeComponent();
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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = (MainViewModel)DataContext;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)
        {
            SearchBox.Focus();
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

    private void RefreshCardPopup()
    {
        var activeCard = _hoveredCard ?? _focusedCard;
        if (activeCard is null)
        {
            CardPopup.IsOpen = false;
            return;
        }

        CardPopup.PlacementTarget = activeCard;
        CardPopup.DataContext = activeCard.DataContext;
        CardPopup.IsOpen = true;
    }

    private void OnCardEnter(object sender, MouseEventArgs e)
    {
        _hoveredCard = (ListBoxItem)sender;
        RefreshCardPopup();
    }

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
}

using Dabom.Library;
using Dabom.Main;
using System.Windows;

namespace Dabom;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var store = new LibraryStore();
        var data = await store.LoadAsync(CancellationToken.None);
        var warning = store.LoadWarning;

        var viewModel = new MainViewModel(store, new LibraryScanner(), data);
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync(warning);
    }
}

using Dabom.Library;
using Dabom.Main;
using Dabom.Metadata;
using System.Net.Http;
using System.Windows;

namespace Dabom;

public partial class App : Application
{
    private readonly HttpClient _httpClient = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var store = new LibraryStore();
        var data = await store.LoadAsync(CancellationToken.None);
        var warning = store.LoadWarning;

        var providers = new IMetadataProvider[]
        {
            new TmdbMetadataProvider(
                _httpClient,
                () => TmdbAccessToken.ReadFromLocalApplicationData(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData)))
        };
        var enrichment = new MetadataEnrichmentService(
            new MediaFilenameParser(),
            providers,
            store,
            _httpClient);
        var viewModel = new MainViewModel(
            store,
            new LibraryScanner(),
            enrichment,
            data);
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync(warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _httpClient.Dispose();
        base.OnExit(e);
    }
}

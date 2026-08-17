using Dabom.Library;
using Dabom.Main;
using Dabom.Metadata;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Dabom;

public partial class App : Application
{
    private readonly HttpClient _httpClient = new();
    private readonly CancellationTokenSource _lifetime = new();

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
                () => LocalEnvironment.ReadFromLocalApplicationData(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "DABOM_TMDB_ACCESS_TOKEN"))
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
            data,
            _lifetime.Token);
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        SetWindowTaskbarIdentity(new WindowInteropHelper(window).EnsureHandle());
        window.Show();
        await viewModel.InitializeAsync(warning);
    }

    internal static void SetWindowTaskbarIdentity(nint windowHandle)
    {
        var interfaceId = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(
            windowHandle,
            ref interfaceId,
            out var propertyStore));
        try
        {
            SetWindowStringProperty(
                propertyStore,
                3,
                $"{Environment.ProcessPath},0");
            SetWindowStringProperty(propertyStore, 5, "Dabom.Dabom");
        }
        finally
        {
            Marshal.ReleaseComObject(propertyStore);
        }
    }

    private static void SetWindowStringProperty(
        IPropertyStore propertyStore,
        uint propertyId,
        string propertyValue)
    {
        var key = new PropertyKey
        {
            FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            PropertyId = propertyId
        };
        var value = new PropVariant
        {
            VariantType = 31,
            PointerValue = Marshal.StringToCoTaskMemUni(propertyValue)
        };
        try
        {
            Marshal.ThrowExceptionForHR(propertyStore.SetValue(ref key, ref value));
            Marshal.ThrowExceptionForHR(propertyStore.Commit());
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime.Cancel();
        _httpClient.Dispose();
        base.OnExit(e);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        internal Guid FormatId;
        internal uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] internal ushort VariantType;
        [FieldOffset(8)] internal nint PointerValue;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(
        nint windowHandle,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int PropVariantClear(ref PropVariant value);
}

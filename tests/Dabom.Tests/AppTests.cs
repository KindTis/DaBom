using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Dabom.Tests;

[TestClass]
public sealed class AppTests
{
    [TestMethod]
    public void SetWindowTaskbarIdentity_AssignsAppIdAndIcon()
    {
        Exception? failure = null;
        string? appUserModelId = null;
        string? relaunchIconResource = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window();
                try
                {
                    var windowHandle = new WindowInteropHelper(window).EnsureHandle();
                    App.SetWindowTaskbarIdentity(windowHandle);
                    appUserModelId = GetWindowProperty(windowHandle, 5);
                    relaunchIconResource = GetWindowProperty(windowHandle, 3);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }

        Assert.AreEqual("Dabom.Dabom", appUserModelId);
        Assert.AreEqual($"{Environment.ProcessPath},0", relaunchIconResource);
    }

    private static string? GetWindowProperty(nint windowHandle, uint propertyId)
    {
        var interfaceId = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(
            windowHandle,
            ref interfaceId,
            out var propertyStore));
        var key = new PropertyKey(
            new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            propertyId);
        Marshal.ThrowExceptionForHR(propertyStore.GetValue(ref key, out var value));
        try
        {
            return value.VariantType == 31
                ? Marshal.PtrToStringUni(value.PointerValue)
                : null;
        }
        finally
        {
            PropVariantClear(ref value);
            Marshal.ReleaseComObject(propertyStore);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort VariantType;

        [FieldOffset(8)]
        public nint PointerValue;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint propertyCount);
        [PreserveSig] int GetAt(uint propertyIndex, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        nint windowHandle,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}

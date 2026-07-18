using System.Runtime.InteropServices;

namespace Dabom.Library;

internal static class WindowsDurationReader
{
    private const ushort VtUi8 = 21;
    private static readonly Guid PropertyStoreGuid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    internal static int PropVariantSize => Marshal.SizeOf<PropVariant>();

    internal static long? TryReadTicks(string path)
    {
        if (PSGetPropertyKeyFromName("System.Media.Duration", out var key) < 0)
        {
            return null;
        }

        IPropertyStore? store = null;
        var variant = new PropVariant();
        try
        {
            var iid = PropertyStoreGuid;
            if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out store) < 0
                || store.GetValue(ref key, out variant) < 0
                || variant.VariantType != VtUi8
                || variant.UInt64Value > long.MaxValue)
            {
                return null;
            }

            return (long)variant.UInt64Value;
        }
        finally
        {
            PropVariantClear(ref variant);
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
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
        [FieldOffset(8)] internal ulong UInt64Value;
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

    [DllImport("propsys.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int PSGetPropertyKeyFromName(string canonicalName, out PropertyKey key);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetPropertyStoreFromParsingName(
        string path,
        IntPtr bindContext,
        uint flags,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int PropVariantClear(ref PropVariant value);
}

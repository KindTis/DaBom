using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;

namespace Dabom.Library;

internal sealed record FileIdentity(
    ulong VolumeSerialNumber,
    ulong FileIdLow,
    ulong FileIdHigh);

internal sealed record FileProbeResult(
    VideoFileStatus Status,
    FileIdentity? Identity);

internal static class WindowsFileOperations
{
    internal static FileProbeResult Probe(string path)
    {
        try
        {
            using var handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandleEx(
                handle,
                FileInfoByHandleClass.FileIdInfo,
                out var information,
                (uint)Marshal.SizeOf<FileIdInfo>())
                ? new(
                    VideoFileStatus.Present,
                    new(
                        information.VolumeSerialNumber,
                        information.FileId.Low,
                        information.FileId.High))
                : new(VideoFileStatus.Unavailable, null);
        }
        catch (Exception error) when (
            error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(VideoFileStatus.Missing, null);
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return new(VideoFileStatus.Unavailable, null);
        }
    }

    internal static void MoveToRecycleBin(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
            Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        FileInfoByHandleClass fileInformationClass,
        out FileIdInfo fileInformation,
        uint bufferSize);

    private enum FileInfoByHandleClass
    {
        FileIdInfo = 18
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileId128
    {
        internal ulong Low;
        internal ulong High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        internal ulong VolumeSerialNumber;
        internal FileId128 FileId;
    }
}

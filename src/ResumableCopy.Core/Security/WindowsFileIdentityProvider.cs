using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Security;

public sealed class WindowsFileIdentityProvider : IFileIdentityProvider
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    public (ulong? VolumeSerial, ulong? FileId) TryGetIdentity(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return (null, null);
        }

        var ioPath = PathNormalization.ExpandForIo(Path.GetFullPath(path));
        var volumeSerial = TryGetVolumeSerial(ioPath);

        using var handle = CreateFile(
            ioPath,
            GenericRead,
            FileShareRead,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return (volumeSerial, null);
        }

        var fileId = TryGetFileId(handle);
        return (volumeSerial, fileId);
    }

    private static ulong? TryGetVolumeSerial(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        if (!GetVolumeInformation(
                root,
                IntPtr.Zero,
                0,
                out var serialNumber,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                0))
        {
            return null;
        }

        return serialNumber;
    }

    private static ulong? TryGetFileId(SafeFileHandle handle)
    {
        var size = Marshal.SizeOf<FileIdInfo>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FILE_INFO_BY_HANDLE_CLASS.FileIdInfo,
                    buffer,
                    (uint)size))
            {
                return null;
            }

            var info = Marshal.PtrToStructure<FileIdInfo>(buffer);
            return BitConverter.ToUInt64(info.FileId, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        FILE_INFO_BY_HANDLE_CLASS fileInformationClass,
        IntPtr lpFileInformation,
        uint dwBufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeInformationW")]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        IntPtr lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        IntPtr lpMaximumComponentLength,
        IntPtr lpFileSystemFlags,
        IntPtr lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    private enum FILE_INFO_BY_HANDLE_CLASS
    {
        FileIdInfo = 18
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] FileId;

        public FileIdInfo()
        {
            FileId = new byte[16];
        }
    }
}

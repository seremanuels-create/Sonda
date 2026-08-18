using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Sonda.Core;

/// <summary>Chiamate Win32 che .NET non espone direttamente.</summary>
internal static partial class Native
{
    // --- Dimensione realmente allocata (file compressi, sparse, segnaposto cloud) ---
    [LibraryImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    private const uint INVALID_FILE_SIZE = 0xFFFFFFFF;

    /// <summary>
    /// Byte effettivamente occupati sul volume da un file (arrotondati al cluster dal file system).
    /// Restituisce -1 se la chiamata fallisce.
    /// </summary>
    public static long GetAllocatedSize(string path)
    {
        uint low = GetCompressedFileSizeW(Extended(path), out uint high);
        if (low == INVALID_FILE_SIZE && Marshal.GetLastWin32Error() != 0) return -1;
        return ((long)high << 32) | low;
    }

    /// <summary>
    /// Forma estesa del percorso (\\?\C:\... o \\?\UNC\server\share\...): niente limite di 260 caratteri e
    /// niente normalizzazione, così le cartelle che finiscono con punto o spazio (create da WSL/git) si aprono.
    /// </summary>
    public static string Extended(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path.Substring(2);
        return @"\\?\" + path;
    }

    // --- Cluster e informazioni del volume ---
    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceW(string lpRootPathName,
        out uint lpSectorsPerCluster, out uint lpBytesPerSector,
        out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);

    public static int GetClusterSize(string rootPath)
    {
        if (GetDiskFreeSpaceW(rootPath, out uint spc, out uint bps, out _, out _))
        {
            long c = (long)spc * bps;
            if (c > 0 && c <= 1 << 25) return (int)c;   // exFAT ammette cluster fino a 32 MB
        }
        return 4096;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceExW(string lpDirectoryName, out ulong lpFreeBytesAvailableToCaller, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);

    public static bool TryGetVolumeSpace(string rootPath, out long total, out long free)
    {
        total = 0; free = 0;
        if (!GetDiskFreeSpaceExW(rootPath, out _, out ulong t, out ulong f)) return false;
        total = (long)t; free = (long)f;
        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(string lpRootPathName,
        System.Text.StringBuilder lpVolumeNameBuffer, int nVolumeNameSize,
        out uint lpVolumeSerialNumber, out uint lpMaximumComponentLength, out uint lpFileSystemFlags,
        System.Text.StringBuilder lpFileSystemNameBuffer, int nFileSystemNameSize);

    public static bool TryGetVolumeInfo(string rootPath, out string label, out string fileSystem)
    {
        var name = new System.Text.StringBuilder(261);
        var fs = new System.Text.StringBuilder(261);
        if (GetVolumeInformationW(rootPath, name, name.Capacity, out _, out _, out _, fs, fs.Capacity))
        {
            label = name.ToString(); fileSystem = fs.ToString();
            return true;
        }
        label = ""; fileSystem = "";
        return false;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string lpszFileName, System.Text.StringBuilder lpszVolumePathName, uint cchBufferLength);

    /// <summary>
    /// Radice del VOLUME che contiene il percorso: "C:\" nel caso normale, ma anche "C:\Mount\Dati\" per un volume
    /// montato in una cartella, o "D:\" se il percorso passa da una giunzione. Path.GetPathRoot è solo sintattico.
    /// </summary>
    public static string GetVolumeRoot(string path)
    {
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            if (GetVolumePathNameW(path, sb, (uint)sb.Capacity))
            {
                string r = sb.ToString();
                if (!r.EndsWith('\\')) r += '\\';
                return r;
            }
        }
        catch { }
        string root = Path.GetPathRoot(path) ?? path;
        return root.EndsWith('\\') ? root : root + '\\';
    }

    // --- Cestino (SHFileOperation con FOF_ALLOWUNDO) ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_NOERRORUI = 0x400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>
    /// Manda nel Cestino i percorsi indicati. Con Windows che mostra la sua finestra di avanzamento.
    /// Ritorna 0 se tutto ok, altrimenti il codice d'errore della shell; 'aborted' se l'utente ha annullato.
    /// </summary>
    public static int SendToRecycleBin(IntPtr owner, IEnumerable<string> paths, bool confirm, out bool aborted)
    {
        // pFrom è una lista doppia-null-terminata
        string from = string.Join('\0', paths) + "\0\0";
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = from,
            pTo = null,
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_WANTNUKEWARNING | (confirm ? 0 : FOF_NOCONFIRMATION)),
        };
        int rc = SHFileOperationW(ref op);
        aborted = op.fAnyOperationsAborted;
        return rc;
    }

    // --- Finestra Proprietà della shell ---
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string pszObjectName, string? pszPropertyPage);
    private const uint SHOP_FILEPATH = 0x2;

    public static bool ShowProperties(string path) => SHObjectProperties(IntPtr.Zero, SHOP_FILEPATH, path, null);

    // --- Elevazione ---
    public static bool IsAdministrator()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // --- Console del processo padre (modalità --report lanciata da terminale) ---
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint dwProcessId);
    public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    // --- Dimensione dell'MFT (metadati NTFS): FSCTL_GET_NTFS_VOLUME_DATA sul volume ---
    [StructLayout(LayoutKind.Sequential)]
    private struct NTFS_VOLUME_DATA_BUFFER
    {
        public long VolumeSerialNumber;
        public long NumberSectors;
        public long TotalClusters;
        public long FreeClusters;
        public long TotalReserved;
        public uint BytesPerSector;
        public uint BytesPerCluster;
        public uint BytesPerFileRecordSegment;
        public uint ClustersPerFileRecordSegment;
        public long MftValidDataLength;
        public long MftStartLcn;
        public long Mft2StartLcn;
        public long MftZoneStart;
        public long MftZoneEnd;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize, out NTFS_VOLUME_DATA_BUFFER lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    private const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;

    /// <summary>Byte occupati dalla tabella MFT (dato reale, non stima). false se non leggibile.</summary>
    public static bool TryGetMftSize(string volRoot, out long mftBytes, out long reservedBytes)
    {
        mftBytes = 0; reservedBytes = 0;
        try
        {
            string dev = @"\\.\" + volRoot.TrimEnd('\\');
            using var h = CreateFileW(dev, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) return false;
            if (!DeviceIoControl(h, FSCTL_GET_NTFS_VOLUME_DATA, IntPtr.Zero, 0, out var buf, (uint)Marshal.SizeOf<NTFS_VOLUME_DATA_BUFFER>(), out _, IntPtr.Zero))
                return false;
            mftBytes = buf.MftValidDataLength;
            reservedBytes = buf.TotalReserved * buf.BytesPerCluster;
            return mftBytes > 0;
        }
        catch { return false; }
    }

    // --- Attributi che .NET non ha nell'enum FileAttributes ---
    public const int FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000;
    public const int FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;
    public const int FILE_ATTRIBUTE_PINNED = 0x00080000;
    public const int FILE_ATTRIBUTE_UNPINNED = 0x00100000;
}

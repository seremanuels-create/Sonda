using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.InteropServices;

namespace Sonda.Core;

/// <summary>
/// Scansione parallela di un volume o di una cartella. Ogni cartella è un lavoro in coda; N thread
/// la enumerano con FileSystemEnumerable (una sola chiamata al kernel per blocco di voci, senza stat per file).
/// </summary>
public sealed class ScanEngine
{
    public ScanStats Live { get; } = new();
    private volatile string _currentPath = "";
    public string CurrentPath => _currentPath;

    // Contatori letti dall'interfaccia durante la scansione
    public long FilesSoFar => Interlocked.Read(ref _files);
    public long DirsSoFar => Interlocked.Read(ref _dirs);
    public long OnDiskSoFar => Interlocked.Read(ref _onDisk);
    public int DeniedSoFar => Volatile.Read(ref _denied);

    private readonly record struct Entry(string Name, bool IsDir, long Length, FileAttributes Attributes, long ModifiedTicks);

    private const FileAttributes NeedsAllocQuery =
        FileAttributes.Compressed | FileAttributes.SparseFile | FileAttributes.Offline | FileAttributes.ReparsePoint |
        (FileAttributes)Native.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS | (FileAttributes)Native.FILE_ATTRIBUTE_RECALL_ON_OPEN;

    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Win32,
        BufferSize = 64 * 1024,
    };

    private BlockingCollection<(DirNode Node, string Path)> _queue = null!;
    private int _pending;
    private int _clusterMask;
    private bool _isNtfs;
    private CancellationToken _ct;
    private long _files, _dirs, _bytes, _onDisk;
    private int _denied, _errors, _reparse, _cloud;

    public ScanResult Run(string path, CancellationToken ct)
    {
        _ct = ct;
        var sw = Stopwatch.StartNew();
        Live.StartedAt = DateTime.Now;

        // "C:" senza barra sarebbe "cartella corrente su C:": qui vuol dire il volume
        if (path.Length == 2 && path[1] == ':') path += '\\';
        string full = Path.GetFullPath(path);
        // Radice del volume vero (segue giunzioni e punti di mount): cluster, formato e spazio libero giusti
        string volRoot = Native.GetVolumeRoot(full);
        bool whole = string.Equals(full.TrimEnd('\\') + "\\", volRoot, StringComparison.OrdinalIgnoreCase);

        var vol = ReadVolume(volRoot);
        _clusterMask = vol.ClusterSize - 1;
        _isNtfs = vol.Format.Equals("NTFS", StringComparison.OrdinalIgnoreCase);

        DirNode root;
        string rootPath;
        if (whole)
        {
            root = new DirNode(volRoot, null) { Flags = DirFlags.Root | DirFlags.VolumeRoot };
            rootPath = volRoot;
        }
        else
        {
            rootPath = full.TrimEnd('\\');
            root = new DirNode(rootPath, null) { Flags = DirFlags.Root };
            var rel = rootPath.Length > volRoot.Length
                ? rootPath.Substring(volRoot.Length).Split('\\', StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();
            root.RootBaseSegments = rel.Select(s => s.ToLowerInvariant()).ToArray();
            root.Depth = rel.Length;
        }
        try
        {
            var di = new DirectoryInfo(rootPath);
            root.Attributes = di.Attributes;
            root.ModifiedTicks = di.LastWriteTimeUtc.Ticks;
        }
        catch { /* non essenziale */ }
        Classifier.ClassifyDir(root);

        _queue = new BlockingCollection<(DirNode, string)>(new ConcurrentQueue<(DirNode, string)>());
        _pending = 1;
        _queue.Add((root, rootPath));

        int workers = Math.Clamp(Environment.ProcessorCount, 4, 16);
        var threads = new Thread[workers];
        for (int i = 0; i < workers; i++)
        {
            threads[i] = new Thread(Worker) { IsBackground = true, Name = $"Sonda-scan-{i}", Priority = ThreadPriority.BelowNormal };
            threads[i].Start();
        }
        foreach (var t in threads) t.Join();

        // Totali del sottoalbero, liste di errori
        var result = new ScanResult
        {
            Root = root,
            Volume = vol,
            Stats = Live,
            ScanPath = rootPath,
            ScannedWholeVolume = whole,
            Elevated = Native.IsAdministrator(),
        };
        Aggregate(root, result);

        sw.Stop();
        Live.Elapsed = sw.Elapsed;
        Live.Cancelled = ct.IsCancellationRequested;
        Live.Files = _files; Live.Dirs = _dirs; Live.Bytes = _bytes; Live.OnDisk = _onDisk;
        Live.DeniedDirs = _denied; Live.ErrorDirs = _errors; Live.ReparseDirs = _reparse; Live.CloudPlaceholders = _cloud;

        if (whole)
        {
            try
            {
                var (bytes, note) = ShadowStorage.Query(volRoot);
                result.ShadowStorageBytes = bytes;
                result.ShadowStorageNote = note;
            }
            catch (Exception ex) { result.ShadowStorageNote = Loc.S("shadow.error", ex.Message); }
        }
        return result;
    }

    private void Worker()
    {
        foreach (var (node, path) in _queue.GetConsumingEnumerable())
        {
            if (!_ct.IsCancellationRequested)
            {
                try { ProcessDir(node, path); }
                catch (Exception ex)
                {
                    node.Flags |= DirFlags.Error;
                    node.ErrorMessage = ex.Message;
                    Interlocked.Increment(ref _errors);
                }
            }
            if (Interlocked.Decrement(ref _pending) == 0) _queue.CompleteAdding();
        }
    }

    private void Enqueue(DirNode node, string path)
    {
        Interlocked.Increment(ref _pending);
        _queue.Add((node, path));
    }

    private void ProcessDir(DirNode node, string path)
    {
        _currentPath = path;
        FileSystemEnumerable<Entry> en;
        try
        {
            // Percorso esteso (\\?\): niente limite 260 e niente normalizzazione (cartelle "nome." o "nome " create da
            // WSL/git altrimenti risulterebbero inesistenti). Il timestamp è l'unica proprietà che può lanciare
            // (FILETIME fuori intervallo): senza il try si perderebbe TUTTO il resto della cartella.
            en = new FileSystemEnumerable<Entry>(Native.Extended(path),
                (ref FileSystemEntry e) =>
                {
                    long t;
                    try { t = e.LastWriteTimeUtc.UtcTicks; } catch (ArgumentOutOfRangeException) { t = 0; }
                    return new Entry(e.FileName.ToString(), e.IsDirectory, e.Length, e.Attributes, t);
                },
                Options);
        }
        catch (Exception ex) { MarkFailure(node, ex); return; }

        List<FileEntry>? files = null;
        long ownSize = 0, ownDisk = 0;
        int fileCount = 0;
        bool sepNeeded = !path.EndsWith('\\');

        IEnumerator<Entry> it;
        try { it = en.GetEnumerator(); }
        catch (Exception ex) { MarkFailure(node, ex); return; }

        using (it)
        {
            while (true)
            {
                Entry e;
                try
                {
                    if (!it.MoveNext()) break;
                    e = it.Current;
                }
                catch (Exception ex) { MarkFailure(node, ex); break; }

                if (e.IsDir)
                {
                    var child = new DirNode(e.Name, node) { Attributes = e.Attributes, ModifiedTicks = e.ModifiedTicks };
                    string childPath = sepNeeded ? path + "\\" + e.Name : path + e.Name;
                    node.Dirs.Add(child);
                    Interlocked.Increment(ref _dirs);

                    if ((e.Attributes & FileAttributes.ReparsePoint) != 0 && IsLinkReparse(childPath))
                    {
                        // Giunzione o link simbolico: il contenuto vive altrove (o su un altro volume). Non si segue.
                        child.Flags |= DirFlags.Reparse;
                        Classifier.ClassifyDir(child);
                        Interlocked.Increment(ref _reparse);
                        continue;
                    }
                    Classifier.ClassifyDir(child);
                    Enqueue(child, childPath);
                }
                else
                {
                    var f = new FileEntry(e.Name, node)
                    {
                        Size = e.Length,
                        Attributes = e.Attributes,
                        ModifiedTicks = e.ModifiedTicks,
                    };
                    long onDisk;
                    int attrs = (int)e.Attributes;
                    bool cloud = (attrs & (Native.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS | Native.FILE_ATTRIBUTE_RECALL_ON_OPEN)) != 0
                                 || (e.Attributes & FileAttributes.Offline) != 0;
                    if (cloud) Interlocked.Increment(ref _cloud);

                    if ((attrs & Native.FILE_ATTRIBUTE_RECALL_ON_OPEN) != 0)
                    {
                        // Aprirlo (anche solo per chiedere la dimensione) lo scaricherebbe dal cloud: non è locale, 0 su disco.
                        onDisk = 0;
                    }
                    else if ((e.Attributes & NeedsAllocQuery) != 0)
                    {
                        string fp = sepNeeded ? path + "\\" + e.Name : path + e.Name;
                        bool sparseOrCompressed = (e.Attributes & (FileAttributes.Compressed | FileAttributes.SparseFile)) != 0;
                        if (!sparseOrCompressed && !cloud && (e.Attributes & FileAttributes.ReparsePoint) != 0 && IsLinkReparse(fp))
                        {
                            // Collegamento simbolico a un file: GetCompressedFileSize seguirebbe il link e conterebbe il bersaglio.
                            onDisk = 0;
                        }
                        else
                        {
                            long a = Native.GetAllocatedSize(fp);
                            // Per i file né compressi né sparse la chiamata dà la dimensione logica: va arrotondata al cluster.
                            onDisk = a < 0 ? RoundUp(e.Length) : sparseOrCompressed ? a : RoundUp(a);
                        }
                    }
                    else onDisk = RoundUp(e.Length);
                    f.OnDisk = onDisk;
                    f.TypeId = Classifier.TypeOf(e.Name);
                    f.Cat = Classifier.ClassifyFile(node, e.Name, f.TypeId);
                    (files ??= new List<FileEntry>()).Add(f);
                    ownSize += e.Length; ownDisk += onDisk; fileCount++;
                }
            }
        }

        node.Files = files;
        node.OwnSize = ownSize;
        node.OwnOnDisk = ownDisk;
        Interlocked.Add(ref _files, fileCount);
        Interlocked.Add(ref _bytes, ownSize);
        Interlocked.Add(ref _onDisk, ownDisk);
    }

    /// <summary>
    /// Byte occupati: multiplo del cluster. File minuscoli (residenti nell'MFT) non consumano cluster.
    /// </summary>
    private long RoundUp(long size)
    {
        if (size <= 0) return 0;
        if (_isNtfs && size <= 700) return 0; // dato residente nel record MFT (solo NTFS)
        long m = _clusterMask;
        return (size + m) & ~m;
    }

    private void MarkFailure(DirNode node, Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            node.Flags |= DirFlags.AccessDenied;
            Interlocked.Increment(ref _denied);
        }
        else
        {
            node.Flags |= DirFlags.Error;
            Interlocked.Increment(ref _errors);
        }
        node.ErrorMessage = ex.Message;
    }

    // ---- reparse tag: solo giunzioni e symlink si saltano; cloud, WCI, projfs si attraversano ----
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    private const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
    private const uint IO_REPARSE_TAG_LX_SYMLINK = 0xA000001D;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATAW lpFindFileData);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr hFindFile);
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    private static bool IsLinkReparse(string path)
    {
        IntPtr h = FindFirstFileW(Native.Extended(path), out var fd);
        if (h == INVALID_HANDLE_VALUE) return true; // in dubbio, non seguire
        FindClose(h);
        if ((fd.dwFileAttributes & 0x400) == 0) return false;
        uint tag = fd.dwReserved0;
        return tag == IO_REPARSE_TAG_MOUNT_POINT || tag == IO_REPARSE_TAG_SYMLINK || tag == IO_REPARSE_TAG_LX_SYMLINK;
    }

    // ---- totali ----
    private static void Aggregate(DirNode node, ScanResult result)
    {
        // iterativo (post-ordine) per non rischiare lo stack su alberi profondi
        var stack = new Stack<(DirNode Node, int Index)>();
        stack.Push((node, 0));
        while (stack.Count > 0)
        {
            var (n, i) = stack.Pop();
            if (i < n.Dirs.Count)
            {
                stack.Push((n, i + 1));
                stack.Push((n.Dirs[i], 0));
                continue;
            }
            long size = n.OwnSize, disk = n.OwnOnDisk;
            int files = n.Files?.Count ?? 0, dirs = 0, denied = 0;
            foreach (var c in n.Dirs)
            {
                size += c.Size; disk += c.OnDisk; files += c.FileCount; dirs += 1 + c.DirCount; denied += c.DeniedCount;
            }
            if (n.IsAccessDenied) { denied++; result.DeniedDirs.Add(n); }
            else if (n.HasError) result.ErrorDirs.Add(n);
            n.Size = size; n.OnDisk = disk; n.FileCount = files; n.DirCount = dirs; n.DeniedCount = denied;
        }
        result.DeniedDirs.Sort((a, b) => string.CompareOrdinal(a.FullPath, b.FullPath));
    }

    private static VolumeInfo ReadVolume(string volRoot)
    {
        var v = new VolumeInfo { RootPath = volRoot };
        // API native: funzionano anche per volumi montati in una cartella (DriveInfo vuole una lettera)
        if (Native.TryGetVolumeSpace(volRoot, out long total, out long free)) { v.TotalBytes = total; v.FreeBytes = free; }
        if (Native.TryGetVolumeInfo(volRoot, out string label, out string fs)) { v.Label = label; v.Format = fs; }
        try
        {
            if (volRoot.Length == 3 && volRoot[1] == ':') v.DriveType = new DriveInfo(volRoot).DriveType;
            else v.DriveType = volRoot.StartsWith(@"\\") ? DriveType.Network : DriveType.Fixed;
        }
        catch { }
        v.ClusterSize = Native.GetClusterSize(volRoot);
        return v;
    }
}

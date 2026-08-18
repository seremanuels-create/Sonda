using System.Text;

namespace Sonda.Core;

[Flags]
public enum DirFlags : byte
{
    None = 0,
    /// <summary>Radice della scansione.</summary>
    Root = 1,
    /// <summary>Accesso negato: contenuto sconosciuto.</summary>
    AccessDenied = 2,
    /// <summary>Punto di giunzione / collegamento simbolico: NON seguito (il contenuto è contato altrove).</summary>
    Reparse = 4,
    /// <summary>Errore di lettura diverso da accesso negato.</summary>
    Error = 8,
    /// <summary>Radice del volume (C:\).</summary>
    VolumeRoot = 16,
}

/// <summary>Una cartella nell'albero della scansione. I totali sono del sottoalbero.</summary>
public sealed class DirNode
{
    public string Name;
    public DirNode? Parent;
    public List<DirNode> Dirs = new(4);
    public List<FileEntry>? Files;

    /// <summary>Byte logici (dimensione dei file) dell'intero sottoalbero.</summary>
    public long Size;
    /// <summary>Byte occupati sul disco (allocati) dell'intero sottoalbero: è questo che consuma l'SSD.</summary>
    public long OnDisk;
    /// <summary>Solo i file direttamente dentro la cartella.</summary>
    public long OwnSize, OwnOnDisk;
    public int FileCount;   // sottoalbero
    public int DirCount;    // sottoalbero (cartelle discendenti)
    public int DeniedCount; // sottoalbero: cartelle non accessibili

    /// <summary>Profondità rispetto alla radice del VOLUME (C:\ = 0, C:\Windows = 1).</summary>
    public int Depth;
    public byte Cat;
    /// <summary>Profondità della cartella in cui è stata assegnata la categoria (per raggruppare).</summary>
    public byte AnchorDepth;
    public byte GroupExtra;
    public DirFlags Flags;
    public FileAttributes Attributes;
    public long ModifiedTicks;
    public string? ErrorMessage;

    public DirNode(string name, DirNode? parent)
    {
        Name = name;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    public bool IsAccessDenied => (Flags & DirFlags.AccessDenied) != 0;
    public bool IsReparse => (Flags & DirFlags.Reparse) != 0;
    public bool IsRoot => (Flags & DirFlags.Root) != 0;
    public bool HasError => (Flags & DirFlags.Error) != 0;

    /// <summary>Percorso completo (ricostruito risalendo i genitori).</summary>
    public string FullPath
    {
        get
        {
            if (Parent is null) return Name;
            var parts = new List<string>(Depth + 1);
            for (DirNode? n = this; n is not null; n = n.Parent) parts.Add(n.Name);
            parts.Reverse();
            var sb = new StringBuilder(parts.Sum(p => p.Length + 1));
            sb.Append(parts[0]);
            if (!parts[0].EndsWith('\\')) sb.Append('\\');
            for (int i = 1; i < parts.Count; i++)
            {
                sb.Append(parts[i]);
                if (i < parts.Count - 1) sb.Append('\\');
            }
            return sb.ToString();
        }
    }

    /// <summary>Segmenti del percorso a partire dalla radice del volume (minuscoli), lunghezza = Depth.</summary>
    public string[] VolumeSegmentsLower()
    {
        var segs = new string[Depth];
        int i = Depth - 1;
        for (DirNode? n = this; n is not null && n.Parent is not null; n = n.Parent)
        {
            segs[i--] = n.Name.ToLowerInvariant();
        }
        // Se la radice della scansione non è la radice del volume, i suoi segmenti sono in RootBaseSegments
        for (DirNode? n = this; n is not null; n = n.Parent)
        {
            if (n.Parent is null && n.RootBaseSegments is not null)
            {
                var b = n.RootBaseSegments;
                for (int k = 0; k < b.Length; k++) segs[k] = b[k];
            }
        }
        return segs;
    }

    /// <summary>Solo per la radice della scansione: segmenti fra la radice del volume e questa cartella (minuscoli).</summary>
    public string[]? RootBaseSegments;

    /// <summary>Antenato alla profondità data (o se stesso).</summary>
    public DirNode AncestorAtDepth(int depth)
    {
        DirNode n = this;
        while (n.Depth > depth && n.Parent is not null) n = n.Parent;
        return n;
    }

    public override string ToString() => FullPath;
}

/// <summary>Un file. Compatto: ce ne possono essere milioni.</summary>
public sealed class FileEntry
{
    public string Name;
    public long Size;
    public long OnDisk;
    public long ModifiedTicks;
    public FileAttributes Attributes;
    public DirNode Dir;
    public byte Cat;
    public ushort TypeId;

    public FileEntry(string name, DirNode dir)
    {
        Name = name;
        Dir = dir;
    }

    public string FullPath => Path.Join(Dir.FullPath, Name);
    public DateTime Modified => new DateTime(ModifiedTicks, DateTimeKind.Utc).ToLocalTime();
    public string Extension
    {
        get
        {
            int i = Name.LastIndexOf('.');
            return i > 0 && i < Name.Length - 1 ? Name.Substring(i).ToLowerInvariant() : string.Empty;
        }
    }
    public bool IsCloudPlaceholder => ((int)Attributes & (Native.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS | Native.FILE_ATTRIBUTE_RECALL_ON_OPEN)) != 0
                                     || (Attributes & FileAttributes.Offline) != 0;
    public bool IsCompressed => (Attributes & FileAttributes.Compressed) != 0;
    public bool IsSparse => (Attributes & FileAttributes.SparseFile) != 0;
}

public sealed class VolumeInfo
{
    public string RootPath = "";        // "C:\"
    public string Label = "";
    public string Format = "";
    public long TotalBytes;
    public long FreeBytes;
    public long UsedBytes => TotalBytes - FreeBytes;
    public int ClusterSize = 4096;
    public bool IsSsd;                  // non sempre determinabile: false = ignoto
    public DriveType DriveType;
    public string Display => string.IsNullOrEmpty(Label) ? RootPath.TrimEnd('\\') : $"{RootPath.TrimEnd('\\')}  {Label}";
}

public sealed class ScanStats
{
    public long Files;
    public long Dirs;
    public long Bytes;
    public long OnDisk;
    public int DeniedDirs;
    public int ErrorDirs;
    public int ReparseDirs;
    public int CloudPlaceholders;
    public TimeSpan Elapsed;
    public DateTime StartedAt;
    public bool Cancelled;
}

public sealed class ScanResult
{
    public required DirNode Root;
    public required VolumeInfo Volume;
    public required ScanStats Stats;
    public required string ScanPath;
    public bool ScannedWholeVolume;
    public bool Elevated;
    /// <summary>Spazio delle copie shadow sul volume, se noto (byte); null = non determinato.</summary>
    public long? ShadowStorageBytes;
    public string? ShadowStorageNote;
    public List<DirNode> DeniedDirs = new();
    public List<DirNode> ErrorDirs = new();
}

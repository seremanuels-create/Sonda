namespace Sonda.Core;

/// <summary>Una "causa" di consumo: una categoria con i suoi numeri, le cartelle e i file che pesano di più.</summary>
public sealed class Cause
{
    public required Category Category { get; init; }
    public long OnDisk;
    public long Size;
    public int Files;
    /// <summary>Quota sullo spazio USATO del volume (o sul totale scansionato se non è l'intero volume).</summary>
    public double Share;
    public List<GroupItem> TopGroups = new();
    public List<FileEntry> TopFiles = new();
    public int Rank;
}

/// <summary>Una cartella "significativa" dentro una causa (es. il singolo programma dentro Program Files).</summary>
public sealed class GroupItem
{
    public required DirNode Dir { get; init; }
    public long OnDisk;
    public int Files;
    public string Path => Dir.FullPath;
    public string Name => Dir.IsRoot ? Dir.Name : Dir.Name;
}

public sealed class TypeStat
{
    public required FileType Type { get; init; }
    public long OnDisk;
    public long Size;
    public int Files;
    public double Share;
    public string? TopExtensions;
}

public sealed class ExtStat
{
    public required string Extension { get; init; }
    public required FileType Type { get; init; }
    public long OnDisk;
    public int Files;
}

public sealed class BalanceLine
{
    public required string Label { get; init; }
    public long? Bytes { get; init; }
    public required string Note { get; init; }
    public bool IsTotal { get; init; }
    public bool IsWarning { get; init; }
}

public sealed class Analysis
{
    public required ScanResult Scan { get; init; }
    public List<Cause> Causes = new();
    public List<FileEntry> TopFiles = new();
    public List<TypeStat> Types = new();
    public List<ExtStat> Extensions = new();
    public List<BalanceLine> Balance = new();
    public long ReferenceBytes;   // denominatore delle percentuali
    public long MftBytes = -1;
    public Cause? Main => Causes.Count > 0 ? Causes[0] : null;

    private const int TopFilesGlobal = 2000;
    private const int TopFilesPerCause = 300;
    private const int TopGroupsPerCause = 60;

    public static Analysis Build(ScanResult scan)
    {
        var a = new Analysis { Scan = scan };
        var root = scan.Root;
        long used = scan.Volume.UsedBytes;
        a.ReferenceBytes = scan.ScannedWholeVolume && used > 0 ? used : Math.Max(1, root.OnDisk);

        int nCat = Classifier.Categories.Length;
        var catDisk = new long[nCat];
        var catSize = new long[nCat];
        var catFiles = new int[nCat];
        var catTop = new PriorityQueue<FileEntry, long>[nCat];
        var groups = new Dictionary<(byte, DirNode), GroupItem>();
        var globalTop = new PriorityQueue<FileEntry, long>(TopFilesGlobal + 1);
        var typeDisk = new Dictionary<ushort, TypeStat>();
        var extDisk = new Dictionary<string, ExtStat>(StringComparer.Ordinal);

        // DFS iterativo
        var stack = new Stack<DirNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            foreach (var c in d.Dirs) stack.Push(c);
            if (d.Files is null || d.Files.Count == 0) continue;

            // cartella di raggruppamento per i file che ereditano la categoria della cartella
            int gDepth = Math.Min(d.Depth, d.AnchorDepth + d.GroupExtra);
            DirNode gDir = d.AncestorAtDepth(gDepth);
            GroupItem? gItem = null;

            foreach (var f in d.Files)
            {
                byte c = f.Cat;
                long od = f.OnDisk;
                catDisk[c] += od; catSize[c] += f.Size; catFiles[c]++;

                // gruppo
                if (c == d.Cat)
                {
                    if (gItem is null)
                    {
                        if (!groups.TryGetValue((c, gDir), out gItem)) { gItem = new GroupItem { Dir = gDir }; groups[(c, gDir)] = gItem; }
                    }
                    gItem.OnDisk += od; gItem.Files++;
                }
                else
                {
                    // file con categoria propria (es. .vhdx dentro AppData): gruppo = la sua cartella
                    if (!groups.TryGetValue((c, d), out var gi)) { gi = new GroupItem { Dir = d }; groups[(c, d)] = gi; }
                    gi.OnDisk += od; gi.Files++;
                }

                // top file
                if (od > 0)
                {
                    var pq = catTop[c] ??= new PriorityQueue<FileEntry, long>(TopFilesPerCause + 1);
                    PushTop(pq, f, od, TopFilesPerCause);
                    PushTop(globalTop, f, od, TopFilesGlobal);
                }

                // tipi
                if (!typeDisk.TryGetValue(f.TypeId, out var ts)) { ts = new TypeStat { Type = Classifier.GetType(f.TypeId) }; typeDisk[f.TypeId] = ts; }
                ts.OnDisk += od; ts.Size += f.Size; ts.Files++;
                string ext = f.Extension;
                if (ext.Length == 0) ext = Loc.S("ext.none");
                if (!extDisk.TryGetValue(ext, out var es)) { es = new ExtStat { Extension = ext, Type = ts.Type }; extDisk[ext] = es; }
                es.OnDisk += od; es.Files++;
            }
        }

        // cause
        for (int c = 0; c < nCat; c++)
        {
            if (catFiles[c] == 0 && catDisk[c] == 0) continue;
            var cause = new Cause { Category = Classifier.Categories[c], OnDisk = catDisk[c], Size = catSize[c], Files = catFiles[c] };
            cause.Share = (double)catDisk[c] / a.ReferenceBytes;
            if (catTop[c] is { } pq) cause.TopFiles = Drain(pq);
            a.Causes.Add(cause);
        }
        foreach (var kv in groups)
        {
            var cause = a.Causes.FirstOrDefault(x => x.Category.Id == kv.Key.Item1);
            cause?.TopGroups.Add(kv.Value);
        }
        foreach (var cause in a.Causes)
        {
            cause.TopGroups.Sort((x, y) => y.OnDisk.CompareTo(x.OnDisk));
            if (cause.TopGroups.Count > TopGroupsPerCause) cause.TopGroups.RemoveRange(TopGroupsPerCause, cause.TopGroups.Count - TopGroupsPerCause);
        }
        // Le cartelle non accessibili sono una "causa" a sé: non sappiamo cosa contengono
        a.Causes.Sort((x, y) => y.OnDisk.CompareTo(x.OnDisk));
        for (int i = 0; i < a.Causes.Count; i++) a.Causes[i].Rank = i + 1;

        a.TopFiles = Drain(globalTop);

        // tipi ed estensioni
        a.Types = typeDisk.Values.OrderByDescending(t => t.OnDisk).ToList();
        foreach (var t in a.Types) t.Share = (double)t.OnDisk / a.ReferenceBytes;
        a.Extensions = extDisk.Values.OrderByDescending(e => e.OnDisk).ToList();
        foreach (var t in a.Types)
        {
            var exts = a.Extensions.Where(e => e.Type == t.Type).Take(6).Select(e => e.Extension);
            t.TopExtensions = string.Join("  ", exts);
        }

        a.BuildBalance();
        return a;
    }

    private static void PushTop(PriorityQueue<FileEntry, long> pq, FileEntry f, long key, int cap)
    {
        if (pq.Count < cap) { pq.Enqueue(f, key); return; }
        if (pq.TryPeek(out _, out long min) && key > min) { pq.Dequeue(); pq.Enqueue(f, key); }
    }

    private static List<FileEntry> Drain(PriorityQueue<FileEntry, long> pq)
    {
        var list = new List<FileEntry>(pq.Count);
        while (pq.TryDequeue(out var f, out _)) list.Add(f);
        list.Reverse();
        return list;
    }

    private void BuildBalance()
    {
        var s = Scan;
        var v = s.Volume;
        var b = Balance;
        if (!s.ScannedWholeVolume)
        {
            b.Add(new BalanceLine { Label = Loc.S("bal.folder"), Bytes = s.Root.OnDisk, Note = s.ScanPath, IsTotal = true });
            b.Add(new BalanceLine { Label = Loc.S("bal.folder.note"), Bytes = null, Note = Loc.S("bal.folder.noteText") });
            return;
        }

        long used = v.UsedBytes;
        b.Add(new BalanceLine
        {
            Label = Loc.S("bal.used"),
            Bytes = used,
            Note = Loc.S("bal.used.note", Format.Bytes(v.TotalBytes), Format.Bytes(v.FreeBytes)),
            IsTotal = true
        });
        b.Add(new BalanceLine
        {
            Label = Loc.S("bal.found"),
            Bytes = s.Root.OnDisk,
            Note = Loc.S("bal.found.note", Format.Count(s.Stats.Files), Format.Count(s.Stats.Dirs), Format.Bytes(s.Root.Size), Format.Count(v.ClusterSize))
        });

        if (s.Stats.Cancelled)
        {
            b.Add(new BalanceLine { Label = Loc.S("bal.cancelled"), Bytes = null, Note = Loc.S("bal.cancelled.note"), IsWarning = true });
            return;
        }

        long explained = s.Root.OnDisk;
        bool ntfs = v.Format.Equals("NTFS", StringComparison.OrdinalIgnoreCase);

        if (Native.TryGetMftSize(v.RootPath, out long mft, out long reserved))
        {
            MftBytes = mft;
            b.Add(new BalanceLine { Label = Loc.S("bal.mft"), Bytes = mft, Note = Loc.S("bal.mft.note") });
            explained += mft;
        }
        else if (ntfs)
        {
            long est = (s.Stats.Files + s.Stats.Dirs) * 1024;
            b.Add(new BalanceLine { Label = Loc.S("bal.mftEst"), Bytes = est, Note = Loc.S("bal.mftEst.note") });
            explained += est;
        }
        else
        {
            b.Add(new BalanceLine { Label = Loc.S("bal.fsMeta", v.Format), Bytes = null, Note = Loc.S("bal.fsMeta.note") });
        }

        if (s.ShadowStorageBytes is long sh)
        {
            b.Add(new BalanceLine { Label = Loc.S("bal.shadow"), Bytes = sh, Note = s.ShadowStorageNote ?? "" });
            explained += sh;
        }
        else
        {
            b.Add(new BalanceLine { Label = Loc.S("bal.shadow"), Bytes = null, Note = s.ShadowStorageNote ?? Loc.S("bal.shadow.unknown"), IsWarning = !s.Elevated });
        }

        if (s.Stats.DeniedDirs > 0)
        {
            b.Add(new BalanceLine
            {
                Label = Loc.S("bal.denied", Format.Count(s.Stats.DeniedDirs)),
                Bytes = null,
                Note = s.Elevated ? Loc.S("bal.denied.admin") : Loc.S("bal.denied.user"),
                IsWarning = true
            });
        }
        if (s.Stats.ReparseDirs > 0)
            b.Add(new BalanceLine { Label = Loc.S("bal.reparse", Format.Count(s.Stats.ReparseDirs)), Bytes = null, Note = Loc.S("bal.reparse.note") });
        if (s.Stats.CloudPlaceholders > 0)
            b.Add(new BalanceLine { Label = Loc.S("bal.cloud", Format.Count(s.Stats.CloudPlaceholders)), Bytes = null, Note = Loc.S("bal.cloud.note") });

        long diff = used - explained;
        if (diff >= 0)
        {
            string note;
            bool warn = diff >= used * 0.02;
            if (s.Stats.DeniedDirs > 0)
            {
                // le più grosse fra le cartelle non accessibili, per dare un indizio concreto
                var hints = s.DeniedDirs.Select(d => d.FullPath)
                    .Where(p => p.EndsWith("\\WindowsApps", StringComparison.OrdinalIgnoreCase)
                             || p.EndsWith("System Volume Information", StringComparison.OrdinalIgnoreCase)
                             || p.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase) && p.Count(c => c == '\\') == 2)
                    .Take(3).ToList();
                note = Loc.S("bal.unattributed.denied",
                    Format.Count(s.Stats.DeniedDirs),
                    hints.Count > 0 ? Loc.S("bal.unattributed.hints", string.Join(", ", hints)) : "",
                    s.Elevated ? Loc.S("bal.unattributed.dot") : Loc.S("bal.unattributed.elevate"));
            }
            else
            {
                note = warn ? Loc.S("bal.unattributed.big") : Loc.S("bal.unattributed.small");
            }
            b.Add(new BalanceLine { Label = Loc.S("bal.unattributed"), Bytes = diff, Note = note, IsWarning = warn });
        }
        else
        {
            b.Add(new BalanceLine { Label = Loc.S("bal.over"), Bytes = -diff, Note = Loc.S("bal.over.note") });
        }
    }
}

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
                if (ext.Length == 0) ext = "(nessuna)";
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
            b.Add(new BalanceLine { Label = "Cartella analizzata", Bytes = s.Root.OnDisk, Note = s.ScanPath, IsTotal = true });
            b.Add(new BalanceLine { Label = "Nota", Bytes = null, Note = "Il bilancio completo (spazio usato del volume contro spazio trovato) è disponibile solo analizzando l'intero volume." });
            return;
        }

        long used = v.UsedBytes;
        b.Add(new BalanceLine { Label = "Spazio usato sul volume", Bytes = used, Note = $"Totale {Format.Bytes(v.TotalBytes)}, liberi {Format.Bytes(v.FreeBytes)}. È il numero di Windows.", IsTotal = true });
        b.Add(new BalanceLine { Label = "File trovati (su disco)", Bytes = s.Root.OnDisk, Note = $"{s.Stats.Files:N0} file in {s.Stats.Dirs:N0} cartelle. Dimensione logica {Format.Bytes(s.Root.Size)}: la differenza è l'arrotondamento al cluster ({v.ClusterSize:N0} byte) meno i file compressi/sparse." });

        if (s.Stats.Cancelled)
        {
            b.Add(new BalanceLine { Label = "Scansione interrotta", Bytes = null, Note = "I file trovati sono parziali: il bilancio non torna per costruzione. Rilancia l'analisi completa per una risposta.", IsWarning = true });
            return;
        }

        long explained = s.Root.OnDisk;
        bool ntfs = v.Format.Equals("NTFS", StringComparison.OrdinalIgnoreCase);

        if (Native.TryGetMftSize(v.RootPath, out long mft, out long reserved))
        {
            MftBytes = mft;
            b.Add(new BalanceLine { Label = "Metadati NTFS (MFT)", Bytes = mft, Note = "Tabella dei file: circa 1 KB per ogni file e cartella. Dato reale letto dal volume." });
            explained += mft;
        }
        else if (ntfs)
        {
            long est = (s.Stats.Files + s.Stats.Dirs) * 1024;
            b.Add(new BalanceLine { Label = "Metadati NTFS (MFT, stima)", Bytes = est, Note = "Stima: 1 KB per ogni file e cartella (il dato reale si legge solo da amministratore)." });
            explained += est;
        }
        else
        {
            b.Add(new BalanceLine { Label = $"Metadati del file system ({v.Format})", Bytes = null, Note = "Su un volume non NTFS i metadati non si stimano; di solito sono trascurabili." });
        }

        if (s.ShadowStorageBytes is long sh)
        {
            b.Add(new BalanceLine { Label = "Copie shadow / punti di ripristino", Bytes = sh, Note = s.ShadowStorageNote ?? "" });
            explained += sh;
        }
        else
        {
            b.Add(new BalanceLine { Label = "Copie shadow / punti di ripristino", Bytes = null, Note = s.ShadowStorageNote ?? "Non determinato.", IsWarning = !s.Elevated });
        }

        if (s.Stats.DeniedDirs > 0)
        {
            b.Add(new BalanceLine
            {
                Label = $"Cartelle non accessibili ({s.Stats.DeniedDirs:N0})",
                Bytes = null,
                Note = s.Elevated
                    ? "Contenuto sconosciuto anche da amministratore (cartelle protette dal sistema)."
                    : "Contenuto sconosciuto: System Volume Information, profili di altri utenti, cartelle protette. Riavvia come amministratore per leggerle.",
                IsWarning = true
            });
        }
        if (s.Stats.ReparseDirs > 0)
            b.Add(new BalanceLine { Label = $"Giunzioni e collegamenti saltati ({s.Stats.ReparseDirs:N0})", Bytes = null, Note = "Puntano ad altre cartelle: il contenuto è contato dove sta davvero (o su un altro volume). Non è spazio in più." });
        if (s.Stats.CloudPlaceholders > 0)
            b.Add(new BalanceLine { Label = $"File cloud solo online ({s.Stats.CloudPlaceholders:N0})", Bytes = null, Note = "Segnaposto di OneDrive & simili: contati per lo spazio che occupano davvero sul disco (spesso zero)." });

        long diff = used - explained;
        if (diff >= 0)
        {
            string note;
            bool warn;
            if (s.Stats.DeniedDirs > 0)
            {
                // le più grosse fra le cartelle non accessibili, per dare un indizio concreto
                var hints = s.DeniedDirs.Select(d => d.FullPath)
                    .Where(p => p.EndsWith("\\WindowsApps", StringComparison.OrdinalIgnoreCase)
                             || p.EndsWith("System Volume Information", StringComparison.OrdinalIgnoreCase)
                             || p.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase) && p.Count(c => c == '\\') == 2)
                    .Take(3).ToList();
                note = $"Sta quasi certamente nelle {s.Stats.DeniedDirs} cartelle non accessibili"
                     + (hints.Count > 0 ? $" (es. {string.Join(", ", hints)})" : "")
                     + (s.Elevated ? "." : ": riavvia come amministratore per attribuirlo.")
                     + " Il resto sono cluster parziali, indici delle cartelle e file aperti in esclusiva.";
                warn = diff >= used * 0.02;
            }
            else
            {
                note = diff < used * 0.02
                    ? "Fisiologico: cluster parziali, indici delle cartelle, file aperti in esclusiva, spazio cambiato durante la scansione."
                    : "Spazio non attribuibile a file: copie shadow non lette, file aperti in modo esclusivo, o spazio cambiato durante la scansione.";
                warn = diff >= used * 0.02;
            }
            b.Add(new BalanceLine { Label = "Non attribuito", Bytes = diff, Note = note, IsWarning = warn });
        }
        else
        {
            b.Add(new BalanceLine
            {
                Label = "Contato più dello spazio usato",
                Bytes = -diff,
                Note = "Normale su Windows: gli hard link fanno comparire lo stesso file in più cartelle (WinSxS e System32 condividono migliaia di file). Lo spazio reale è quello usato dal volume; le voci Windows sono da leggere come limite superiore.",
            });
        }
    }
}

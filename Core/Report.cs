using System.Globalization;
using System.Text;

namespace Sonda.Core;

/// <summary>Esportazioni: CSV (per Excel, separatore ';') e rapporto di testo.</summary>
public static class Report
{
    private static readonly Encoding Utf8Bom = new UTF8Encoding(true);

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Excel interpreta come formula i campi che iniziano con = + - @: un file "-backup.zip" o un nome
        // costruito ad arte ("=cmd|...") diventerebbe una formula. Un apostrofo davanti li rende testo.
        if (s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;
        if (s.IndexOfAny([';', '"', '\n', '\r']) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public static void WriteFilesCsv(string path, IEnumerable<FileEntry> files)
    {
        using var w = new StreamWriter(path, false, Utf8Bom);
        w.WriteLine(Loc.S("csv.files"));
        foreach (var f in files)
        {
            var cat = Classifier.Get(f.Cat);
            var t = Classifier.GetType(f.TypeId);
            w.Write(Csv(f.Name)); w.Write(';');
            w.Write(Csv(f.Dir.FullPath)); w.Write(';');
            w.Write(f.OnDisk.ToString(CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(Csv(Format.Bytes(f.OnDisk))); w.Write(';');
            w.Write(f.Size.ToString(CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(Csv(t.Label)); w.Write(';');
            w.Write(Csv(Classifier.Describe(f))); w.Write(';');
            w.Write(Csv(cat.Name)); w.Write(';');
            w.Write(Csv(Format.SafetyLabel(cat.Safety))); w.Write(';');
            w.Write(Csv(Format.Date(f.Modified))); w.Write(';');
            w.WriteLine(Csv(f.FullPath));
        }
    }

    public static void WriteDirsCsv(string path, IEnumerable<DirNode> dirs)
    {
        using var w = new StreamWriter(path, false, Utf8Bom);
        w.WriteLine(Loc.S("csv.dirs"));
        foreach (var d in dirs)
        {
            var cat = Classifier.Get(d.Cat);
            string note = d.IsAccessDenied ? Loc.S("note.deniedShort")
                        : d.IsReparse ? Loc.S("note.reparseCsv")
                        : d.HasError ? Loc.S("note.error", d.ErrorMessage ?? "") : "";
            w.Write(Csv(d.Name)); w.Write(';');
            w.Write(d.OnDisk.ToString(CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(Csv(Format.Bytes(d.OnDisk))); w.Write(';');
            w.Write(d.Size.ToString(CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(d.FileCount.ToString(CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(d.DirCount.ToString(CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(Csv(cat.Name)); w.Write(';');
            w.Write(Csv(Format.SafetyLabel(cat.Safety))); w.Write(';');
            w.Write(Csv(note)); w.Write(';');
            w.WriteLine(Csv(d.FullPath));
        }
    }

    public static void WriteCausesCsv(string path, Analysis a)
    {
        using var w = new StreamWriter(path, false, Utf8Bom);
        w.WriteLine(Loc.S("csv.causes"));
        foreach (var c in a.Causes)
        {
            w.Write(c.Rank); w.Write(';');
            w.Write(Csv(c.Category.Name)); w.Write(';');
            w.Write(c.OnDisk.ToString(CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(Csv(Format.Bytes(c.OnDisk))); w.Write(';');
            w.Write(Csv(Format.Percent(c.Share))); w.Write(';');
            w.Write(c.Files); w.Write(';');
            w.Write(Csv(c.Category.Description)); w.Write(';');
            w.Write(Csv(c.Category.Action)); w.Write(';');
            w.WriteLine(Csv(Format.SafetyLabel(c.Category.Safety)));
        }
    }

    /// <summary>Rapporto completo leggibile (usato anche dalla modalità --report).</summary>
    public static string BuildText(Analysis a, int topFiles = 100, int topGroups = 15)
    {
        var s = a.Scan;
        var v = s.Volume;
        var sb = new StringBuilder();
        sb.AppendLine(Loc.S("rep.title"));
        sb.AppendLine(new string('=', 72));
        sb.AppendLine(Loc.S("rep.scanned", s.ScanPath, s.Stats.StartedAt.ToString(Loc.IsItalian ? "dd/MM/yyyy HH:mm" : "yyyy-MM-dd HH:mm", Loc.Culture), Format.Duration(s.Stats.Elapsed)));
        sb.AppendLine(Loc.S("rep.volume", v.Display, v.Format, Format.Bytes(v.TotalBytes), Format.Bytes(v.UsedBytes),
            Format.Percent((double)v.UsedBytes / Math.Max(1, v.TotalBytes)), Format.Bytes(v.FreeBytes)));
        sb.AppendLine(Loc.S("rep.found", Format.Count(s.Stats.Files), Format.Count(s.Stats.Dirs), Format.Bytes(s.Root.OnDisk), Format.Bytes(s.Root.Size)));
        if (s.Stats.DeniedDirs > 0)
            sb.AppendLine(Loc.S("rep.denied", s.Stats.DeniedDirs) + (s.Elevated ? "" : Loc.S("rep.denied.hint")));
        if (s.Stats.Cancelled) sb.AppendLine(Loc.S("rep.cancelled"));
        sb.AppendLine();

        string ofWhat = s.ScannedWholeVolume ? Loc.S("rep.ofUsed") : Loc.S("rep.ofFolder");

        if (a.Main is { } main)
        {
            sb.AppendLine(Loc.S("rep.mainCause"));
            sb.AppendLine(new string('-', 72));
            sb.AppendLine(Loc.S("rep.mainLine", main.Category.Name, Format.Bytes(main.OnDisk), Format.Percent(main.Share), ofWhat, Format.Count(main.Files)));
            sb.AppendLine(Loc.S("rep.whatIs", main.Category.Description));
            sb.AppendLine(Loc.S("rep.howTo", main.Category.Action));
            sb.AppendLine(Loc.S("rep.safety", Format.SafetyLabel(main.Category.Safety)));
            sb.AppendLine(Loc.S("rep.topFolders"));
            foreach (var g in main.TopGroups.Take(topGroups))
                sb.AppendLine($"    {Format.Bytes(g.OnDisk),12}  {g.Path}");
            sb.AppendLine();
        }

        sb.AppendLine(Loc.S("rep.otherCauses"));
        sb.AppendLine(new string('-', 72));
        foreach (var c in a.Causes.Skip(1))
        {
            sb.AppendLine($"  {c.Rank,2}. {c.Category.Name,-42} {Format.Bytes(c.OnDisk),12}  {Format.Percent(c.Share),6}  {Format.Count(c.Files),10} {Loc.S("fmt.files")}  [{Format.SafetyLabel(c.Category.Safety)}]");
            foreach (var g in c.TopGroups.Take(5))
                sb.AppendLine($"        {Format.Bytes(g.OnDisk),12}  {g.Path}");
        }
        sb.AppendLine();

        sb.AppendLine(Loc.S("rep.topFiles", topFiles));
        sb.AppendLine(new string('-', 72));
        foreach (var f in a.TopFiles.Take(topFiles))
        {
            sb.AppendLine($"  {Format.Bytes(f.OnDisk),12}  {f.FullPath}");
            sb.AppendLine($"                {Classifier.Describe(f)} - {Classifier.Get(f.Cat).Name}");
        }
        sb.AppendLine();

        sb.AppendLine(Loc.S("rep.types"));
        sb.AppendLine(new string('-', 72));
        foreach (var t in a.Types.Take(25))
            sb.AppendLine($"  {t.Type.Label,-32} {Format.Bytes(t.OnDisk),12}  {Format.Percent(t.Share),6}  {Format.Count(t.Files),10} {Loc.S("fmt.files")}   {t.TopExtensions}");
        sb.AppendLine();

        sb.AppendLine(Loc.S("rep.balance"));
        sb.AppendLine(new string('-', 72));
        foreach (var l in a.Balance)
            sb.AppendLine($"  {l.Label,-44} {(l.Bytes is long bb ? Format.Bytes(bb) : "-"),12}  {l.Note}");
        sb.AppendLine();

        if (s.DeniedDirs.Count > 0)
        {
            sb.AppendLine(Loc.S("rep.deniedList"));
            sb.AppendLine(new string('-', 72));
            foreach (var d in s.DeniedDirs.Take(200)) sb.AppendLine("  " + d.FullPath);
            if (s.DeniedDirs.Count > 200) sb.AppendLine(Loc.S("rep.andMore", s.DeniedDirs.Count - 200));
        }
        return sb.ToString();
    }
}

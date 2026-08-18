using System.Globalization;
using System.Text;

namespace Sonda.Core;

/// <summary>Esportazioni: CSV (per Excel, separatore ';') e rapporto di testo.</summary>
public static class Report
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");
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
        w.WriteLine("Nome;Cartella;Su disco (byte);Su disco;Dimensione (byte);Tipo;Cosa è;Categoria;Sicurezza;Modificato;Percorso completo");
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
        w.WriteLine("Cartella;Su disco (byte);Su disco;Dimensione (byte);File;Sottocartelle;Categoria;Sicurezza;Note;Percorso completo");
        foreach (var d in dirs)
        {
            var cat = Classifier.Get(d.Cat);
            string note = d.IsAccessDenied ? "accesso negato" : d.IsReparse ? "giunzione/collegamento (non seguito)" : d.HasError ? "errore: " + d.ErrorMessage : "";
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
        w.WriteLine("Posizione;Causa;Su disco (byte);Su disco;Quota;File;Cos'è;Come liberare;Sicurezza");
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
        sb.AppendLine("SONDA - rapporto sullo spazio occupato");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine($"Analizzato: {s.ScanPath}   il {s.Stats.StartedAt:dd/MM/yyyy HH:mm}   in {Format.Duration(s.Stats.Elapsed)}");
        sb.AppendLine($"Volume {v.Display} ({v.Format}): totale {Format.Bytes(v.TotalBytes)}, usati {Format.Bytes(v.UsedBytes)} ({Format.Percent((double)v.UsedBytes / Math.Max(1, v.TotalBytes))}), liberi {Format.Bytes(v.FreeBytes)}");
        sb.AppendLine($"Trovati {Format.Count(s.Stats.Files)} file in {Format.Count(s.Stats.Dirs)} cartelle: {Format.Bytes(s.Root.OnDisk)} su disco ({Format.Bytes(s.Root.Size)} logici)");
        if (s.Stats.DeniedDirs > 0) sb.AppendLine($"Cartelle non accessibili: {s.Stats.DeniedDirs}{(s.Elevated ? "" : "  (avvia come amministratore per leggerle)")}");
        if (s.Stats.Cancelled) sb.AppendLine("ATTENZIONE: scansione interrotta, dati parziali.");
        sb.AppendLine();

        if (a.Main is { } main)
        {
            sb.AppendLine("CAUSA PRINCIPALE");
            sb.AppendLine(new string('-', 72));
            string ofWhat = s.ScannedWholeVolume ? "dello spazio usato" : "della cartella analizzata";
            sb.AppendLine($"  {main.Category.Name}: {Format.Bytes(main.OnDisk)} ({Format.Percent(main.Share)} {ofWhat}), {Format.Count(main.Files)} file");
            sb.AppendLine($"  Cos'è: {main.Category.Description}");
            sb.AppendLine($"  Come liberare: {main.Category.Action}");
            sb.AppendLine($"  Sicurezza: {Format.SafetyLabel(main.Category.Safety)}");
            sb.AppendLine("  Cartelle che pesano di più:");
            foreach (var g in main.TopGroups.Take(topGroups))
                sb.AppendLine($"    {Format.Bytes(g.OnDisk),12}  {g.Path}");
            sb.AppendLine();
        }

        sb.AppendLine("ALTRE CAUSE (in ordine di peso)");
        sb.AppendLine(new string('-', 72));
        foreach (var c in a.Causes.Skip(1))
        {
            sb.AppendLine($"  {c.Rank,2}. {c.Category.Name,-42} {Format.Bytes(c.OnDisk),12}  {Format.Percent(c.Share),6}  {Format.Count(c.Files),10} file  [{Format.SafetyLabel(c.Category.Safety)}]");
            foreach (var g in c.TopGroups.Take(5))
                sb.AppendLine($"        {Format.Bytes(g.OnDisk),12}  {g.Path}");
        }
        sb.AppendLine();

        sb.AppendLine($"I {topFiles} FILE PIÙ GRANDI");
        sb.AppendLine(new string('-', 72));
        foreach (var f in a.TopFiles.Take(topFiles))
        {
            var t = Classifier.GetType(f.TypeId);
            sb.AppendLine($"  {Format.Bytes(f.OnDisk),12}  {f.FullPath}");
            sb.AppendLine($"                {Classifier.Describe(f)} - {Classifier.Get(f.Cat).Name}");
        }
        sb.AppendLine();

        sb.AppendLine("TIPI DI FILE");
        sb.AppendLine(new string('-', 72));
        foreach (var t in a.Types.Take(25))
            sb.AppendLine($"  {t.Type.Label,-32} {Format.Bytes(t.OnDisk),12}  {Format.Percent(t.Share),6}  {Format.Count(t.Files),10} file   {t.TopExtensions}");
        sb.AppendLine();

        sb.AppendLine("BILANCIO");
        sb.AppendLine(new string('-', 72));
        foreach (var l in a.Balance)
            sb.AppendLine($"  {l.Label,-44} {(l.Bytes is long bb ? Format.Bytes(bb) : "-"),12}  {l.Note}");
        sb.AppendLine();

        if (s.DeniedDirs.Count > 0)
        {
            sb.AppendLine("CARTELLE NON ACCESSIBILI");
            sb.AppendLine(new string('-', 72));
            foreach (var d in s.DeniedDirs.Take(200)) sb.AppendLine("  " + d.FullPath);
            if (s.DeniedDirs.Count > 200) sb.AppendLine($"  ... e altre {s.DeniedDirs.Count - 200}");
        }
        return sb.ToString();
    }
}

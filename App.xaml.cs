using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using Sonda.Core;

namespace Sonda;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Numeri e date all'italiana in tutta l'interfaccia
        var it = CultureInfo.GetCultureInfo("it-IT");
        CultureInfo.DefaultThreadCurrentCulture = it;
        CultureInfo.DefaultThreadCurrentUICulture = it;
        Thread.CurrentThread.CurrentCulture = it;
        Thread.CurrentThread.CurrentUICulture = it;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(it.IetfLanguageTag)));

        // Modalita' rapporto da riga di comando:
        //   Sonda.exe --report C:\ [--out rapporto.txt] [--csv cartella]
        var args = e.Args;
        int ri = Array.FindIndex(args, a => a.Equals("--report", StringComparison.OrdinalIgnoreCase));
        if (ri >= 0)
        {
            RunReport(args, ri);
            Shutdown(0);
            return;
        }

        // Primo argomento posizionale che non sia il valore di un'opzione (--shot x.png, --tab 2, --out, --csv)
        string? initialPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) { i++; continue; }
            initialPath = args[i]; break;
        }
        // "C:" (senza barra: nella riga di comando "C:\" farebbe da escape alle virgolette) -> "C:\"
        if (initialPath is { Length: 2 } && initialPath[1] == ':') initialPath += "\\";
        // Diagnostica: --shot file.png [--tab N]  analizza, salva l'immagine della finestra ed esce
        var win = new MainWindow(initialPath) { ShotPath = ArgValue(args, "--shot") };
        if (int.TryParse(ArgValue(args, "--tab"), out int tab)) win.ShotTab = tab;
        MainWindow = win;
        win.Show();
    }

    private static void RunReport(string[] args, int ri)
    {
        Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
        string path = ri + 1 < args.Length && !args[ri + 1].StartsWith("--", StringComparison.Ordinal) ? args[ri + 1] : "C:\\";
        string? outFile = ArgValue(args, "--out");
        string? csvDir = ArgValue(args, "--csv");

        var engine = new ScanEngine();
        Console.Error.WriteLine($"Sonda: analisi di {path} ...");
        var result = engine.Run(path, CancellationToken.None);
        var analysis = Analysis.Build(result);
        string text = Report.BuildText(analysis, topFiles: 200, topGroups: 25);

        if (outFile is not null) File.WriteAllText(outFile, text, System.Text.Encoding.UTF8);
        else Console.Out.Write(text);

        if (csvDir is not null)
        {
            Directory.CreateDirectory(csvDir);
            Report.WriteFilesCsv(Path.Combine(csvDir, "file-piu-grandi.csv"), analysis.TopFiles);
            Report.WriteCausesCsv(Path.Combine(csvDir, "cause.csv"), analysis);
            var dirs = new List<DirNode>();
            var stack = new Stack<DirNode>(); stack.Push(result.Root);
            while (stack.Count > 0) { var d = stack.Pop(); dirs.Add(d); foreach (var c in d.Dirs) stack.Push(c); }
            dirs.Sort((a, b) => b.OnDisk.CompareTo(a.OnDisk));
            Report.WriteDirsCsv(Path.Combine(csvDir, "cartelle.csv"), dirs.Take(5000));
        }
        Console.Error.WriteLine($"Sonda: fatto in {Format.Duration(result.Stats.Elapsed)} ({Format.Count(result.Stats.Files)} file).");
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

using System.Diagnostics;

namespace Sonda.Core;

/// <summary>Operazioni verso la shell di Windows: apri in Esplora risorse, Cestino, elevazione.</summary>
public static class ShellOps
{
    // Eseguibili di sistema con percorso completo: con "explorer.exe" nudo CreateProcess cercherebbe prima nella
    // cartella di Sonda.exe (scrivibile dall'utente, e Sonda può girare elevato).
    private static string SystemExe(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), name);
    private static string System32Exe(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);

    /// <summary>Apre Esplora risorse con il file/cartella selezionato.</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path))
                Process.Start(new ProcessStartInfo(SystemExe("explorer.exe"), $"/select,\"{path}\"") { UseShellExecute = false });
            else
            {
                string? parent = Path.GetDirectoryName(path);
                if (parent is not null && Directory.Exists(parent))
                    Process.Start(new ProcessStartInfo(SystemExe("explorer.exe"), $"\"{parent}\"") { UseShellExecute = false });
            }
        }
        catch { }
    }

    /// <summary>Apre la cartella in Esplora risorse (dentro).</summary>
    public static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(SystemExe("explorer.exe"), $"\"{path}\"") { UseShellExecute = false });
        }
        catch { }
    }

    /// <summary>Apre la finestra Proprietà di Windows (che calcola la dimensione anche lui, utile per confronto).</summary>
    public static void OpenProperties(string path)
    {
        // Il verbo "properties" non esiste per ShellExecute (serve SEE_MASK_INVOKEIDLIST): si usa l'API apposta.
        try { Native.ShowProperties(path); } catch { }
    }

    /// <summary>Riavvia il programma come amministratore. Ritorna false se l'utente rifiuta.</summary>
    public static bool RestartElevated(string? args = null)
    {
        try
        {
            string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
            var psi = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", Arguments = args ?? "" };
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Manda al Cestino. Ritorna null se ok, altrimenti un messaggio d'errore.</summary>
    public static string? Recycle(IntPtr owner, IReadOnlyList<string> paths, bool confirm = true)
    {
        int rc = Native.SendToRecycleBin(owner, paths, confirm, out bool aborted);
        if (aborted) return "Operazione annullata.";
        if (rc == 0) return null;
        // Codici DE_* di SHFileOperation (shellapi.h)
        return rc switch
        {
            0x71 => "Origine e destinazione coincidono (DE_SAMEFILE).",
            0x72 => "Più origini per una sola destinazione.",
            0x73 => "Le cartelle sono su volumi diversi.",
            0x74 => "Non si può eliminare la radice di un disco (DE_ROOTDIR).",
            0x75 => "Annullato dall'utente.",
            0x76 => "La destinazione è una sottocartella dell'origine.",
            0x78 => "Accesso negato all'origine: serve avviare come amministratore o il file è protetto (DE_ACCESSDENIEDSRC).",
            0x79 => "Percorso troppo lungo (DE_PATHTOODEEP).",
            0x7A => "Percorso non valido o unità inesistente.",
            0x7C => "Percorso non valido (DE_INVALIDFILES).",
            0x7D => "La destinazione è uguale all'origine.",
            0x7E => "Il file esiste già nella destinazione.",
            0x80 => "La destinazione è una cartella, non un file (DE_FILEDESTISFLD).",
            0x81 => "Nome file troppo lungo.",
            0x82 => "Il disco è in sola lettura (CD-ROM).",
            0x83 => "Il disco è in sola lettura (DVD).",
            0x84 => "Il disco è in sola lettura (CD-R).",
            0x85 => "Il file è più grande di quanto il file system ammetta.",
            0x86 => "Errore di accesso all'origine.",
            0x87 => "Errore di accesso alla destinazione.",
            0x88 => "Il file è in uso da un altro programma o l'operazione non è ammessa.",
            0x10000 => "Errore di lettura/scrittura del disco.",
            5 => "Accesso negato: serve avviare come amministratore.",
            2 => "File non trovato (forse già eliminato).",
            32 => "Il file è in uso da un altro programma.",
            _ => $"Errore della shell (codice 0x{rc:X}).",
        };
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Apre "Impostazioni > Archiviazione" o Pulizia disco.</summary>
    public static void OpenStorageSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true }); } catch { }
    }

    public static void OpenDiskCleanup(string volRoot)
    {
        try { Process.Start(new ProcessStartInfo(System32Exe("cleanmgr.exe"), "/d " + volRoot.TrimEnd('\\')) { UseShellExecute = true }); } catch { }
    }
}

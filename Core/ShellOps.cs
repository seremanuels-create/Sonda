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
        if (aborted) return Loc.S("shell.cancelled");
        if (rc == 0) return null;
        // Codici DE_* di SHFileOperation (shellapi.h)
        return rc switch
        {
            0x71 => Loc.S("shell.sameFile"),
            0x72 => Loc.S("shell.manySrc"),
            0x73 => Loc.S("shell.diffVolumes"),
            0x74 => Loc.S("shell.rootDir"),
            0x75 => Loc.S("shell.userCancel"),
            0x76 => Loc.S("shell.destSubtree"),
            0x78 => Loc.S("shell.accessSrc"),
            0x79 => Loc.S("shell.pathTooDeep"),
            0x7A => Loc.S("shell.badPath"),
            0x7C => Loc.S("shell.invalidFiles"),
            0x7D => Loc.S("shell.sameDest"),
            0x7E => Loc.S("shell.exists"),
            0x80 => Loc.S("shell.destIsFolder"),
            0x81 => Loc.S("shell.nameTooLong"),
            0x82 or 0x83 or 0x84 => Loc.S("shell.readOnly"),
            0x85 => Loc.S("shell.tooBig"),
            0x86 => Loc.S("shell.srcError"),
            0x87 => Loc.S("shell.dstError"),
            0x88 => Loc.S("shell.inUse2"),
            0x10000 => Loc.S("shell.diskError"),
            5 => Loc.S("shell.denied"),
            2 => Loc.S("shell.notFound"),
            32 => Loc.S("shell.inUse"),
            _ => Loc.S("shell.generic", rc.ToString("X")),
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

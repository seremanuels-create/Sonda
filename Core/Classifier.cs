namespace Sonda.Core;

public enum Safety : byte
{
    /// <summary>Non eliminare a mano: fa parte del sistema o serve ai programmi.</summary>
    Keep = 0,
    /// <summary>Valutare caso per caso.</summary>
    Review = 1,
    /// <summary>Si può eliminare o svuotare senza conseguenze (si ricrea o non serve).</summary>
    Deletable = 2,
}

/// <summary>Famiglia di categorie: decide il colore.</summary>
public enum Family : byte { Other = 0, Windows, Programs, AppData, Personal, Dev, System, VirtualDisks }

public sealed class Category
{
    public required byte Id { get; init; }
    public required string Name { get; init; }
    public required Family Family { get; init; }
    /// <summary>Cos'è.</summary>
    public required string Description { get; init; }
    /// <summary>Come si libera spazio.</summary>
    public required string Action { get; init; }
    public required Safety Safety { get; init; }
    public override string ToString() => Name;
}

public sealed class FileType
{
    public required ushort Id { get; init; }
    public required string Label { get; init; }      // es. "Video", "Disco virtuale"
    public required string Detail { get; init; }     // es. "File video MP4"
    public override string ToString() => Label;
}

/// <summary>
/// Assegna a cartelle e file una CATEGORIA (dove sta e perché occupa spazio: la "causa")
/// e ai file un TIPO (cos'è, dall'estensione).
/// </summary>
public static class Classifier
{
    // ------------------------------------------------------------------ categorie
    public const byte Other = 0, WindowsSys = 1, WinSxS = 2, WinUpdate = 3, WinInstaller = 4, WinTemp = 5,
        Logs = 6, Drivers = 7, WindowsOld = 8, PageFile = 9, Hiberfil = 10, SwapFile = 11, RecycleBin = 12,
        RestorePoints = 13, Programs = 14, UserPrograms = 15, StoreApps = 16, InstallerCache = 17, Games = 18,
        AppData = 19, Caches = 20, UserTemp = 21, DevCaches = 22, DevBuild = 23, GitRepos = 24, DevSdk = 25,
        VirtualDisks = 26, DiskImages = 27, Downloads = 28, Personal = 29, OneDrive = 30, UserOther = 31,
        Mail = 32, Archives = 33, SearchIndex = 34, Fonts = 35;

    public static readonly Category[] Categories =
    [
        C(Other, "Altro", Family.Other, Safety.Review,
            "File e cartelle fuori dai posti standard di Windows (cartelle create a mano alla radice, ecc.).",
            "Controlla a mano: apri la cartella e guarda cosa contiene."),
        C(WindowsSys, "Sistema Windows", Family.Windows, Safety.Keep,
            "Il sistema operativo: System32, SysWOW64, servizi, font, .NET, ambiente di ripristino.",
            "Non eliminare nulla a mano. Usa Impostazioni > Sistema > Archiviazione > Consigli per la pulizia, oppure Pulizia disco (cleanmgr)."),
        C(WinSxS, "Archivio componenti (WinSxS)", Family.Windows, Safety.Keep,
            "Windows\\WinSxS: tutte le versioni dei componenti di sistema, comprese quelle sostituite dagli aggiornamenti. ATTENZIONE: la dimensione mostrata è lorda. Molti file sono hard link condivisi con System32, quindi lo spazio davvero occupato è inferiore (spesso la metà).",
            "Da amministratore: Dism /Online /Cleanup-Image /AnalyzeComponentStore per la dimensione reale, poi Dism /Online /Cleanup-Image /StartComponentCleanup per pulire. Mai eliminare file a mano."),
        C(WinUpdate, "Windows Update (download e cache)", Family.Windows, Safety.Review,
            "Windows\\SoftwareDistribution: pacchetti di aggiornamento scaricati e il database di Windows Update.",
            "Pulizia disco > 'Pulizia di Windows Update'. In alternativa: ferma il servizio (net stop wuauserv), svuota SoftwareDistribution\\Download, riavvia il servizio."),
        C(WinInstaller, "Installer di Windows (MSI)", Family.Windows, Safety.Keep,
            "Windows\\Installer: i pacchetti MSI/MSP dei programmi installati, conservati per poterli riparare o disinstallare.",
            "Non eliminare a mano: i programmi non si disinstallerebbero più. Se è enorme, uno strumento come PatchCleaner individua i pacchetti orfani."),
        C(WinTemp, "File temporanei di sistema", Family.Windows, Safety.Deletable,
            "Windows\\Temp, CbsTemp e simili: file di lavoro di installazioni e aggiornamenti.",
            "Impostazioni > Sistema > Archiviazione > File temporanei, oppure svuota Windows\\Temp (i file in uso vengono saltati)."),
        C(Logs, "Log, dump e segnalazioni errori", Family.Windows, Safety.Deletable,
            "Log di installazione e aggiornamento, dump di memoria (MEMORY.DMP, Minidump), Windows Error Reporting, tracce ETL.",
            "Pulizia disco > 'File di dump della memoria' e 'File di segnalazione errori'. I file .log e .dmp si possono eliminare."),
        C(Drivers, "Archivio driver (DriverStore)", Family.Windows, Safety.Keep,
            "Windows\\System32\\DriverStore: tutti i driver installati, comprese le versioni vecchie (soprattutto GPU).",
            "Non eliminare a mano. pnputil /enum-drivers elenca i pacchetti; strumenti come DriverStore Explorer rimuovono i duplicati vecchi."),
        C(WindowsOld, "Installazione precedente (Windows.old)", Family.Windows, Safety.Deletable,
            "La versione precedente di Windows, tenuta dieci giorni dopo un aggiornamento importante per poter tornare indietro.",
            "Pulizia disco > 'Installazioni precedenti di Windows', oppure Impostazioni > Archiviazione > File temporanei."),
        C(PageFile, "Memoria virtuale (pagefile.sys)", Family.System, Safety.Keep,
            "File di paging: è l'estensione della RAM su disco. Windows ne decide la dimensione in base alla RAM.",
            "Non eliminare. Per ridurlo: Impostazioni avanzate di sistema > Prestazioni > Memoria virtuale (meglio lasciarlo automatico)."),
        C(Hiberfil, "Ibernazione (hiberfil.sys)", Family.System, Safety.Review,
            "Copia della memoria RAM usata per l'ibernazione e per l'avvio rapido: è grande circa quanto la RAM.",
            "Se non usi l'ibernazione: da amministratore 'powercfg /h off' lo elimina (perdi anche l'avvio rapido). 'powercfg /h /type reduced' lo dimezza tenendo l'avvio rapido."),
        C(SwapFile, "Swap delle app (swapfile.sys)", Family.System, Safety.Keep,
            "File di scambio usato dalle app di Microsoft Store. Di solito piccolo.",
            "Gestito da Windows, non serve intervenire."),
        C(RecycleBin, "Cestino", Family.System, Safety.Deletable,
            "File eliminati ma non ancora rimossi dal disco ($Recycle.Bin).",
            "Svuota il Cestino (tasto destro sull'icona > Svuota cestino)."),
        C(RestorePoints, "Punti di ripristino e copie shadow", Family.System, Safety.Review,
            "System Volume Information: punti di ripristino, copie shadow (versioni precedenti dei file), indice e database di sistema. Leggibile solo da amministratore.",
            "Protezione sistema (sysdm.cpl) > Configura > 'Elimina' o riduci lo spazio riservato. Da amministratore: vssadmin delete shadows /for=C: /oldest."),
        C(Programs, "Programmi installati", Family.Programs, Safety.Review,
            "Program Files e Program Files (x86): i programmi installati per tutti gli utenti.",
            "Disinstalla da Impostazioni > App > App installate quelli che non usi. Non eliminare le cartelle a mano."),
        C(UserPrograms, "Programmi installati per l'utente", Family.Programs, Safety.Review,
            "AppData\\Local\\Programs: programmi installati solo per il tuo utente (VS Code, Discord, Signal, ecc.).",
            "Disinstalla da Impostazioni > App."),
        C(StoreApps, "App di Microsoft Store e loro dati", Family.Programs, Safety.Review,
            "WindowsApps (le app) e AppData\\Local\\Packages (dati e cache delle app dello Store e di sistema).",
            "Disinstalla le app da Impostazioni > App. Le sottocartelle di Packages contengono i dati delle app: 'Reimposta' l'app dalle sue Opzioni avanzate."),
        C(InstallerCache, "Cache di installer", Family.Programs, Safety.Review,
            "ProgramData\\Package Cache (Visual Studio, .NET, SQL: installer conservati per riparazioni) e cartelle di installer estratti alla radice (NVIDIA, AMD, Intel).",
            "Le cartelle NVIDIA/AMD/Intel alla radice si possono eliminare a installazione finita. Package Cache no: usa Visual Studio Installer per rimuovere carichi di lavoro."),
        C(Games, "Giochi", Family.Programs, Safety.Review,
            "Librerie di Steam, Epic Games, GOG, Xbox e cartelle 'Games'.",
            "Disinstalla i giochi dal loro launcher (Steam > Gestisci > Disinstalla), non a mano."),
        C(AppData, "Dati e cache delle applicazioni", Family.AppData, Safety.Review,
            "AppData (Local, Roaming, LocalLow) e ProgramData: impostazioni, database, modelli, cache dei programmi installati.",
            "Guarda quale applicazione pesa di più: spesso ha una cache svuotabile dalle sue impostazioni (Teams, Spotify, Slack, editor...)."),
        C(Caches, "Cache di browser e app", Family.AppData, Safety.Deletable,
            "Cache di Chrome, Edge, Firefox, Brave, Opera e cache di app (Code Cache, GPUCache, shader cache, INetCache).",
            "Nel browser: Cancella dati di navigazione > 'Immagini e file memorizzati nella cache'. Le cartelle Cache si possono eliminare a programma chiuso: si ricreano."),
        C(UserTemp, "File temporanei utente", Family.AppData, Safety.Deletable,
            "AppData\\Local\\Temp: file di lavoro lasciati da installer e programmi.",
            "Impostazioni > Archiviazione > File temporanei, oppure svuota %TEMP% a mano (i file in uso vengono saltati)."),
        C(DevCaches, "Cache di pacchetti di sviluppo", Family.Dev, Safety.Deletable,
            "Cache locali di npm, pip, NuGet, Gradle, Maven, Cargo, Go, pnpm, Yarn, uv/poetry: pacchetti scaricati, riscaricabili.",
            "npm cache clean --force  ·  pip cache purge  ·  dotnet nuget locals all --clear  ·  yarn cache clean  ·  pnpm store prune  ·  Gradle: elimina ~\\.gradle\\caches"),
        C(DevBuild, "Dipendenze e build dei progetti", Family.Dev, Safety.Deletable,
            "node_modules, cartelle build/obj/.vs/_deps: si ricreano compilando o con npm install.",
            "Elimina node_modules e le cartelle di build dei progetti che non stai usando; si rigenerano quando servono."),
        C(GitRepos, "Repository Git (.git)", Family.Dev, Safety.Review,
            "Cronologia completa dei repository clonati.",
            "git gc --aggressive nel repository, oppure elimina i cloni che non ti servono più."),
        C(DevSdk, "SDK e strumenti di sviluppo", Family.Dev, Safety.Review,
            "Android SDK, immagini di sistema degli emulatori, toolchain, JDK.",
            "Da SDK Manager rimuovi immagini di sistema, emulatori e versioni di piattaforma che non usi."),
        C(VirtualDisks, "Dischi virtuali (VM, WSL, Docker)", Family.VirtualDisks, Safety.Review,
            "File .vhdx/.vhd/.vmdk/.vdi: dischi di macchine virtuali, di WSL e di Docker Desktop. Crescono con l'uso e NON si restringono da soli quando cancelli dentro.",
            "WSL: wsl --shutdown poi compatta il vhdx (Optimize-VHD o diskpart 'compact vdisk'). Docker: docker system prune -a. VM: compatta il disco dal gestore VM."),
        C(DiskImages, "Immagini disco e ISO", Family.VirtualDisks, Safety.Deletable,
            "File .iso, .img, .wim, .esd: immagini di installazione e dischi.",
            "Elimina le ISO che hai già usato o che puoi riscaricare."),
        C(Downloads, "Download", Family.Personal, Safety.Deletable,
            "La cartella Download: installer, archivi e file scaricati che spesso non servono più.",
            "Elimina gli installer (.exe/.msi) e gli archivi già usati; sposta altrove quello che vuoi tenere."),
        C(Personal, "Documenti e file personali", Family.Personal, Safety.Review,
            "Documenti, Immagini, Video, Musica, Desktop.",
            "Sposta su un disco esterno o nel cloud ciò che non ti serve a portata di mano; i video sono quasi sempre la voce più pesante."),
        C(OneDrive, "OneDrive", Family.Personal, Safety.Review,
            "Cartella OneDrive: i file 'sempre disponibili su questo dispositivo' occupano spazio locale; quelli 'solo online' no.",
            "Tasto destro sui file/cartelle > 'Libera spazio' per tenerli solo nel cloud."),
        C(UserOther, "Profilo utente (altro)", Family.Personal, Safety.Review,
            "Altre cartelle dentro C:\\Users\\<utente> (progetti, cartelle create a mano, .cache di strumenti).",
            "Controlla a mano cosa contengono."),
        C(Mail, "Posta di Outlook (.pst/.ost)", Family.AppData, Safety.Review,
            "Archivi e cache della posta di Outlook.",
            "In Outlook riduci il periodo di 'Posta da tenere offline' o archivia le cartelle vecchie."),
        C(Archives, "Archivi compressi", Family.Other, Safety.Review,
            "File .zip, .7z, .rar, .tar.gz sparsi.",
            "Elimina gli archivi già estratti."),
        C(SearchIndex, "Indice di ricerca di Windows", Family.Windows, Safety.Keep,
            "ProgramData\\Microsoft\\Search: il database dell'indicizzazione dei file.",
            "Non eliminare a mano. Opzioni di indicizzazione > Avanzate > Ricostruisci, o riduci le posizioni indicizzate."),
        C(Fonts, "Font", Family.Windows, Safety.Keep,
            "Caratteri installati (Windows\\Fonts).",
            "Disinstalla i font che non usi da Impostazioni > Personalizzazione > Tipi di carattere."),
    ];

    private static Category C(byte id, string name, Family fam, Safety s, string desc, string action) =>
        new() { Id = id, Name = name, Family = fam, Safety = s, Description = desc, Action = action };

    public static Category Get(byte id) => id < Categories.Length ? Categories[id] : Categories[0];

    // ------------------------------------------------------------------ regole per cartelle
    // Regola ancorata alla radice del volume: sequenza di segmenti (minuscoli); "*" = un segmento qualsiasi;
    // "xxx*" = segmento che inizia con xxx.
    private sealed record Rule(string[] Segs, byte Cat, byte GroupExtra);

    private static readonly Rule[] RootRules = Build(
        // radice
        R("$recycle.bin", RecycleBin, 0),
        R("system volume information", RestorePoints, 0),
        R("windows.old", WindowsOld, 0),
        R("$windows.~bt", WindowsOld, 0),
        R("$windows.~ws", WindowsOld, 0),
        R("$getcurrent", WinUpdate, 0),
        R("recovery", WindowsSys, 0),
        R("perflogs", Logs, 0),
        R("config.msi", WinInstaller, 0),
        R("msocache", InstallerCache, 0),
        R("nvidia", InstallerCache, 0),
        R("amd", InstallerCache, 0),
        R("intel", InstallerCache, 0),
        R("dell", InstallerCache, 0),
        R("hp", InstallerCache, 0),
        R("swsetup", InstallerCache, 0),
        R("drivers", InstallerCache, 0),
        R("steamlibrary", Games, 3),   // D:\SteamLibrary\steamapps\common\<Gioco>
        R("games", Games, 1),
        R("xboxgames", Games, 1),
        R("epic games", Games, 1),
        R("gog games", Games, 1),
        R("riot games", Games, 1),
        R("onedrivetemp", OneDrive, 0),
        // Windows
        R("windows", WindowsSys, 1),
        R("windows/winsxs", WinSxS, 0),
        R("windows/servicing/lcu", WinSxS, 0),
        R("windows/softwaredistribution", WinUpdate, 1),
        R("windows/installer", WinInstaller, 0),
        R("windows/temp", WinTemp, 0),
        R("windows/cbstemp", WinTemp, 0),
        R("windows/downloaded program files", WinTemp, 0),
        R("windows/prefetch", Logs, 0),          // si rigenera da solo, Pulizia disco lo tratta come eliminabile
        R("windows/logs", Logs, 1),
        R("windows/minidump", Logs, 0),
        R("windows/livekernelreports", Logs, 0),
        R("windows/panther", Logs, 0),
        R("windows/system32/logfiles", Logs, 1),
        R("windows/system32/winevt", Logs, 0),
        R("windows/system32/driverstore", Drivers, 0),
        R("windows/system32/drivers", WindowsSys, 0),
        R("windows/fonts", Fonts, 0),
        R("windows/microsoft.net", WindowsSys, 0),
        R("windows/assembly", WindowsSys, 0),
        R("windows/containers", VirtualDisks, 1),
        R("windows/system32/config/systemprofile/appdata/local/temp", WinTemp, 0),
        // Programmi
        R("program files", Programs, 1),
        R("program files (x86)", Programs, 1),
        R("program files/windowsapps", StoreApps, 1),
        R("program files/wsl", VirtualDisks, 0),
        R("program files/docker", VirtualDisks, 1),
        R("program files (x86)/steam/steamapps", Games, 2),
        R("program files/steam/steamapps", Games, 2),
        R("program files/epic games", Games, 1),
        R("program files (x86)/epic games", Games, 1),
        R("program files (x86)/gog galaxy/games", Games, 1),
        R("program files/gog galaxy/games", Games, 1),
        R("program files (x86)/origin games", Games, 1),
        R("program files/ea games", Games, 1),
        R("program files (x86)/ubisoft/ubisoft game launcher/games", Games, 1),
        R("program files/riot games", Games, 1),
        R("program files (x86)/battle.net", Games, 1),
        // Blizzard & co. installano i giochi direttamente in Program Files (x86)\<Gioco>
        R("program files (x86)/overwatch", Games, 0),
        R("program files (x86)/hearthstone", Games, 0),
        R("program files (x86)/world of warcraft", Games, 0),
        R("program files (x86)/diablo*", Games, 0),
        R("program files (x86)/starcraft*", Games, 0),
        R("program files (x86)/heroes of the storm", Games, 0),
        R("program files (x86)/warcraft*", Games, 0),
        R("program files (x86)/call of duty*", Games, 0),
        R("program files/overwatch", Games, 0),
        R("program files/world of warcraft", Games, 0),
        R("program files/diablo*", Games, 0),
        R("program files/call of duty*", Games, 0),
        R("program files (x86)/minecraft launcher", Games, 0),
        R("program files (x86)/rockstar games", Games, 1),
        R("program files/rockstar games", Games, 1),
        R("program files (x86)/electronic arts", Games, 1),
        R("program files/electronic arts", Games, 1),
        R("program files (x86)/2k games", Games, 1),
        R("program files (x86)/bethesda.net launcher/games", Games, 1),
        R("program files/wargaming.net", Games, 1),
        R("program files (x86)/world of tanks", Games, 0),
        R("program files (x86)/fortnite", Games, 0),
        R("program files/oculus", Games, 1),
        R("program files/oculus/software", Games, 1),
        R("program files/modifiablewindowsapps", StoreApps, 1),
        R("program files/android", DevSdk, 1),
        R("program files/java", DevSdk, 1),
        R("program files/dotnet", DevSdk, 0),
        R("program files/nodejs", DevSdk, 0),
        R("program files/microsoft visual studio", DevSdk, 1),
        R("program files (x86)/microsoft visual studio", DevSdk, 1),
        R("program files (x86)/windows kits", DevSdk, 1),
        R("program files (x86)/android", DevSdk, 1),
        // ProgramData
        R("programdata", AppData, 1),
        R("programdata/package cache", InstallerCache, 0),
        R("programdata/microsoft/windows/wer", Logs, 0),
        R("programdata/microsoft/search", SearchIndex, 0),
        R("programdata/microsoft/windows defender/scans", AppData, 0),
        R("programdata/docker", VirtualDisks, 0),
        R("programdata/dockerdesktop", VirtualDisks, 0),
        R("programdata/microsoft/windows/containers", VirtualDisks, 0),
        R("programdata/usoshared/logs", Logs, 0),
        R("programdata/nvidia corporation/downloader", InstallerCache, 0),
        R("programdata/nvidia corporation/nv_cache", Caches, 0),
        R("programdata/nvidia", Caches, 0),
        R("programdata/microsoft/diagnosis", Logs, 0),
        R("programdata/microsoft/windows/systemdata", WindowsSys, 0),
        R("programdata/microsoft/windows/retaildemo", WinTemp, 0),   // contenuto "demo negozio", Pulizia disco lo rimuove
        R("programdata/microsoft/windows/wsl", VirtualDisks, 0),
        R("programdata/regid.*", AppData, 0),
        R("programdata/adobe", AppData, 1),
        R("programdata/chocolatey", DevSdk, 0),
        R("programdata/scoop", DevSdk, 0),
        R("programdata/npm-cache", DevCaches, 0),
        // Utenti
        R("users", UserOther, 1),
        R("users/*", UserOther, 1),
        R("users/*/downloads", Downloads, 0),
        R("users/*/download", Downloads, 0),
        R("users/*/documents", Personal, 0),
        R("users/*/documenti", Personal, 0),
        R("users/*/pictures", Personal, 0),
        R("users/*/immagini", Personal, 0),
        R("users/*/videos", Personal, 0),
        R("users/*/video", Personal, 0),
        R("users/*/music", Personal, 0),
        R("users/*/musica", Personal, 0),
        R("users/*/desktop", Personal, 0),
        R("users/*/onedrive*", OneDrive, 0),
        R("users/*/appdata", AppData, 2),
        R("users/*/appdata/local/temp", UserTemp, 0),
        R("users/*/appdata/local/programs", UserPrograms, 1),
        R("users/*/appdata/local/packages", StoreApps, 1),
        R("users/*/appdata/local/packages/canonicalgrouplimited*", VirtualDisks, 0),
        R("users/*/appdata/local/microsoft/windowsapps", StoreApps, 0),
        R("users/*/appdata/local/microsoft/windows/inetcache", Caches, 0),
        R("users/*/appdata/local/microsoft/windows/webcache", Caches, 0),
        R("users/*/appdata/local/microsoft/windows/explorer", Caches, 0),
        R("users/*/appdata/local/microsoft/windows/wer", Logs, 0),
        R("users/*/appdata/local/microsoft/windows/fonts", Fonts, 0),
        R("users/*/appdata/local/microsoft/onedrive", AppData, 0),
        R("users/*/appdata/local/microsoft/outlook", Mail, 0),
        R("users/*/appdata/local/microsoft/teams", AppData, 0),
        R("users/*/appdata/local/microsoft/edge/user data", AppData, 1),
        R("users/*/appdata/local/microsoft/edge/user data/*/cache", Caches, 0),
        R("users/*/appdata/local/microsoft/edge/user data/*/code cache", Caches, 0),
        R("users/*/appdata/local/microsoft/edge/user data/*/service worker", Caches, 0),
        R("users/*/appdata/local/google/chrome/user data", AppData, 1),
        R("users/*/appdata/local/google/chrome/user data/*/cache", Caches, 0),
        R("users/*/appdata/local/google/chrome/user data/*/code cache", Caches, 0),
        R("users/*/appdata/local/google/chrome/user data/*/service worker", Caches, 0),
        R("users/*/appdata/local/bravesoftware/brave-browser/user data/*/cache", Caches, 0),
        R("users/*/appdata/local/bravesoftware/brave-browser/user data/*/code cache", Caches, 0),
        R("users/*/appdata/local/vivaldi/user data/*/cache", Caches, 0),
        R("users/*/appdata/local/opera software/opera stable/cache", Caches, 0),
        R("users/*/appdata/local/opera software/opera gx stable/cache", Caches, 0),
        R("users/*/appdata/local/mozilla/firefox/profiles/*/cache2", Caches, 0),
        R("users/*/appdata/local/mozilla/firefox/profiles/*/startupcache", Caches, 0),
        R("users/*/appdata/local/crashdumps", Logs, 0),
        R("users/*/appdata/local/d3dscache", Caches, 0),
        R("users/*/appdata/local/nvidia", Caches, 0),
        // AMD e Intel tengono anche impostazioni (Radeon CN, profili): eliminabili solo le cache dentro
        R("users/*/appdata/local/amd", AppData, 0),
        R("users/*/appdata/local/amd/dxcache", Caches, 0),
        R("users/*/appdata/local/amd/glcache", Caches, 0),
        R("users/*/appdata/local/amd/vkcache", Caches, 0),
        R("users/*/appdata/local/intel", AppData, 0),
        R("users/*/appdata/local/intel/shadercache", Caches, 0),
        R("users/*/appdata/local/npm-cache", DevCaches, 0),
        R("users/*/appdata/roaming/npm-cache", DevCaches, 0),
        R("users/*/appdata/roaming/npm", DevSdk, 0),
        R("users/*/appdata/local/pip", DevCaches, 0),
        R("users/*/appdata/local/nuget", DevCaches, 0),
        R("users/*/appdata/local/pnpm", DevCaches, 0),
        R("users/*/appdata/local/pnpm-cache", DevCaches, 0),
        R("users/*/appdata/local/yarn", DevCaches, 0),
        R("users/*/appdata/local/pypoetry", DevCaches, 0),
        R("users/*/appdata/local/uv", DevCaches, 0),
        R("users/*/appdata/local/go-build", DevCaches, 0),
        R("users/*/appdata/local/electron", DevCaches, 0),
        R("users/*/appdata/local/electron-builder", DevCaches, 0),
        R("users/*/appdata/local/node-gyp", DevCaches, 0),
        R("users/*/appdata/local/ms-playwright", DevSdk, 0),
        R("users/*/appdata/local/microsoft/visualstudio", AppData, 1),
        R("users/*/appdata/local/microsoft/vscode-cpptools", DevCaches, 0),
        R("users/*/appdata/local/jetbrains", AppData, 1),
        R("users/*/appdata/local/android", DevSdk, 1),
        R("users/*/appdata/local/google/androidstudio*", DevSdk, 0),
        R("users/*/appdata/local/docker", VirtualDisks, 0),
        R("users/*/appdata/local/temp/*", UserTemp, 0),
        R("users/*/appdata/roaming/code/cache", Caches, 0),
        R("users/*/appdata/roaming/code/cacheddata", Caches, 0),
        R("users/*/appdata/roaming/code/cachedextensionvsixs", Caches, 0),
        R("users/*/appdata/roaming/code/user/workspacestorage", Caches, 0),
        R("users/*/appdata/roaming/microsoft/teams", AppData, 0),
        R("users/*/appdata/roaming/spotify", AppData, 0),
        R("users/*/appdata/roaming/discord/cache", Caches, 0),
        R("users/*/appdata/roaming/discord/code cache", Caches, 0),
        R("users/*/appdata/roaming/slack/cache", Caches, 0),
        R("users/*/appdata/locallow", AppData, 1),
        // Le cartelle "punto" degli strumenti contengono anche configurazione e binari: eliminabile è solo la cache dentro
        R("users/*/.nuget", DevSdk, 0),
        R("users/*/.nuget/packages", DevCaches, 0),
        R("users/*/.nuget/v3-cache", DevCaches, 0),
        R("users/*/.gradle", DevSdk, 0),
        R("users/*/.gradle/caches", DevCaches, 0),
        R("users/*/.gradle/wrapper", DevCaches, 0),
        R("users/*/.gradle/daemon", DevCaches, 0),
        R("users/*/.m2", DevSdk, 0),
        R("users/*/.m2/repository", DevCaches, 0),
        R("users/*/.cargo", DevSdk, 0),
        R("users/*/.cargo/registry", DevCaches, 0),
        R("users/*/.cargo/git", DevCaches, 0),
        R("users/*/.rustup", DevSdk, 0),
        R("users/*/.dotnet", DevSdk, 0),
        R("users/*/.android", DevSdk, 0),
        R("users/*/.docker", VirtualDisks, 0),
        R("users/*/.wsl", VirtualDisks, 0),
        R("users/*/.vscode", AppData, 0),
        R("users/*/.cache", DevCaches, 0),
        R("users/*/.npm", DevCaches, 0),
        R("users/*/.pyenv", DevSdk, 0),
        R("users/*/.conda", DevSdk, 0),
        R("users/*/anaconda3", DevSdk, 0),
        R("users/*/miniconda3", DevSdk, 0),
        R("users/*/go/pkg/mod", DevCaches, 0),
        R("users/*/scoop", DevSdk, 1),
        R("users/*/source", UserOther, 1),
        R("users/*/appdata/local/microsoft/windows/inetcookies", Caches, 0),
        R("users/public", Personal, 1),
        R("users/default", WindowsSys, 0),
        R("users/all users", AppData, 1),
        // hyper-v default
        R("programdata/microsoft/windows/virtual hard disks", VirtualDisks, 0),
        R("programdata/microsoft/windows/hyper-v", VirtualDisks, 0),
        R("users/public/documents/hyper-v/virtual hard disks", VirtualDisks, 0),
        R("virtual machines", VirtualDisks, 1),
        R("vms", VirtualDisks, 1),
        R("hyper-v", VirtualDisks, 1),
        R("wsl", VirtualDisks, 1),
        R("inetpub", Programs, 0),
        R("xampp", Programs, 0),
        R("python*", DevSdk, 0),
        R("msys64", DevSdk, 0),
        R("cygwin64", DevSdk, 0),
        R("mingw64", DevSdk, 0),
        R("android", DevSdk, 1),
        R("flutter", DevSdk, 0),
        R("dev", UserOther, 1),
        R("progetti", UserOther, 1),
        R("projects", UserOther, 1),
        R("src", UserOther, 1),
        R("temp", WinTemp, 0),
        R("tmp", WinTemp, 0)
    );

    // Regole "ovunque": si applicano al nome della cartella stessa, ma solo se la categoria ereditata
    // è fra quelle "libere" (non dentro Windows, Program Files, ecc.).
    private static readonly Dictionary<string, (byte Cat, byte Extra)> AnywhereRules = new(StringComparer.Ordinal)
    {
        ["node_modules"] = (DevBuild, 0),
        [".git"] = (GitRepos, 0),
        [".vs"] = (DevBuild, 0),
        ["_deps"] = (DevBuild, 0),
        ["obj"] = (DevBuild, 0),
        [".gradle"] = (DevCaches, 0),
        [".nuget"] = (DevCaches, 0),
        [".cargo"] = (DevCaches, 0),
        [".venv"] = (DevBuild, 0),
        ["venv"] = (DevBuild, 0),
        ["__pycache__"] = (DevBuild, 0),
        [".pytest_cache"] = (DevBuild, 0),
        [".mypy_cache"] = (DevBuild, 0),
        [".next"] = (DevBuild, 0),
        [".nuxt"] = (DevBuild, 0),
        [".angular"] = (DevBuild, 0),
        [".parcel-cache"] = (DevBuild, 0),
        [".turbo"] = (DevBuild, 0),
        ["cmake-build-debug"] = (DevBuild, 0),
        ["cmake-build-release"] = (DevBuild, 0),
        ["cache"] = (Caches, 0),
        ["caches"] = (Caches, 0),
        ["cache2"] = (Caches, 0),
        ["code cache"] = (Caches, 0),
        ["gpucache"] = (Caches, 0),
        ["dxcache"] = (Caches, 0),
        ["glcache"] = (Caches, 0),
        ["vkcache"] = (Caches, 0),
        ["dawncache"] = (Caches, 0),
        ["shadercache"] = (Caches, 0),
        ["cachestorage"] = (Caches, 0),
        ["cacheddata"] = (Caches, 0),
        ["cachedextensions"] = (Caches, 0),
        ["service worker"] = (Caches, 0),
        ["crashpad"] = (Logs, 0),
        ["crashdumps"] = (Logs, 0),
        ["logs"] = (Logs, 0),
        ["log"] = (Logs, 0),
        ["node_modules.bak"] = (DevBuild, 0),
        ["target"] = (DevBuild, 0),      // Rust/Maven
        ["dist"] = (DevBuild, 0),
        ["build"] = (DevBuild, 0),
        ["out"] = (DevBuild, 0),
        ["packages"] = (DevCaches, 0),
    };

    // Le regole "ovunque" generiche (build, bin, dist, out, debug, release, x64, target, packages, logs, log,
    // cache...) valgono solo se la cartella è dentro un contesto "libero": profilo utente o cartelle
    // fuori standard. Quelle molto specifiche (node_modules, .git, __pycache__, .venv...) valgono in più contesti.
    private static readonly HashSet<string> SpecificAnywhere = new(StringComparer.Ordinal)
        { "node_modules", ".git", ".vs", "_deps", "__pycache__", ".pytest_cache", ".mypy_cache", ".venv", "venv", ".next", ".nuxt", ".angular", ".parcel-cache", ".turbo", "cmake-build-debug", "cmake-build-release", "node_modules.bak", ".gradle", ".nuget", ".cargo" };

    // "bin", "debug", "release", "x64" NON ci sono: troppo spesso contengono binari veri (es. mysql\bin) e non build.
    private static readonly HashSet<string> GenericBuildNames = new(StringComparer.Ordinal)
        { "target", "dist", "build", "out", "obj", "packages" };

    // ------------------------------------------------------------------ tipi di file
    private static readonly List<FileType> Types = new();
    private static readonly Dictionary<string, ushort> ExtToType = new(StringComparer.Ordinal);
    public static readonly ushort TypeUnknown, TypeNoExt, TypeVirtualDisk, TypeDiskImage, TypeMail, TypeArchive, TypeLog, TypeDump, TypeRegistry;

    static Classifier()
    {
        TypeUnknown = T("File", "File di tipo non riconosciuto");
        TypeNoExt = T("File senza estensione", "File senza estensione");
        void Ext(string label, string detail, params string[] exts)
        {
            ushort id = T(label, detail);
            foreach (var e in exts)
            {
                // Una chiave doppia sovrascriverebbe in silenzio: meglio saperlo subito.
                if (!ExtToType.TryAdd(e, id)) throw new InvalidOperationException($"Estensione dichiarata due volte nella tabella dei tipi: {e}");
            }
        }
        TypeVirtualDisk = T("Disco virtuale", "Disco di macchina virtuale, WSL o Docker (.vhdx/.vhd/.vmdk/.vdi)");
        foreach (var e in new[] { ".vhdx", ".vhd", ".avhdx", ".vmdk", ".vdi", ".qcow2", ".hdd", ".vbox-prev" }) ExtToType[e] = TypeVirtualDisk;
        TypeDiskImage = T("Immagine disco", "Immagine di disco o di installazione (.iso/.img/.wim/.esd)");
        foreach (var e in new[] { ".iso", ".img", ".wim", ".esd", ".swm", ".dmg", ".nrg", ".mds", ".cue" }) ExtToType[e] = TypeDiskImage;
        TypeMail = T("Archivio di posta", "Archivio Outlook (.pst/.ost)");
        foreach (var e in new[] { ".pst", ".ost" }) ExtToType[e] = TypeMail;
        TypeArchive = T("Archivio compresso", "Archivio compresso (.zip/.7z/.rar/.tar.gz)");
        foreach (var e in new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".cab", ".arj", ".lz", ".lzma", ".z" }) ExtToType[e] = TypeArchive;
        TypeLog = T("Log", "File di registro (log)");
        foreach (var e in new[] { ".log", ".etl", ".evtx", ".txt.log", ".trace" }) ExtToType[e] = TypeLog;
        TypeDump = T("Dump di memoria", "Dump di memoria dopo un errore (.dmp)");
        foreach (var e in new[] { ".dmp", ".mdmp", ".hdmp", ".kdmp" }) ExtToType[e] = TypeDump;
        // Hive del registro e loro log transazionali: NON sono log di testo e non vanno toccati.
        TypeRegistry = T("Registro di sistema", "Hive del registro di Windows o suo log transazionale: non eliminare");
        foreach (var e in new[] { ".hve", ".hiv", ".log1", ".log2", ".blf", ".regtrans-ms" }) ExtToType[e] = TypeRegistry;

        // NB: ogni estensione va dichiarata UNA volta sola (Ext lancia se doppia). ".ts" è video, non TypeScript:
        // per un analizzatore di spazio conta chi pesa. ".dat" e ".idx" sono generici e stanno in "Sistema"/"Database".
        Ext("Video", "File video", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".mpg", ".mpeg", ".webm", ".flv", ".ts", ".m2ts", ".mts", ".3gp", ".vob", ".ogv", ".divx", ".mxf", ".braw", ".r3d");
        Ext("Audio", "File audio", ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma", ".aif", ".aiff", ".opus", ".alac", ".ape", ".dsf", ".mid", ".midi", ".wv", ".caf");
        Ext("Progetto audio", "Progetto/sessione di software musicale", ".als", ".flp", ".cpr", ".logicx", ".ptx", ".rpp", ".song", ".reason", ".band", ".sesx", ".dawproject");
        Ext("Campioni / librerie audio", "Librerie di suoni e campioni", ".nki", ".nkx", ".nkc", ".nkm", ".nkr", ".ncw", ".sf2", ".sfz", ".exs", ".kontakt", ".nicnt", ".soundbank", ".ufs", ".upl", ".vsn", ".rex", ".rx2");
        Ext("Immagine", "File immagine", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".psd", ".psb", ".ai", ".svg", ".ico", ".xcf", ".kra", ".avif", ".jxl", ".exr", ".hdr", ".tga");
        Ext("Documento", "Documento", ".pdf", ".doc", ".docx", ".odt", ".rtf", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp", ".pages", ".numbers", ".key", ".epub", ".mobi", ".azw3", ".djvu", ".one", ".xps", ".pub", ".md", ".txt", ".csv", ".tsv");
        Ext("Programma (eseguibile)", "Programma eseguibile o installer", ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".appinstaller", ".com", ".scr");
        Ext("Libreria / componente", "Libreria di programma (DLL, componente, driver)", ".dll", ".ocx", ".drv", ".cpl", ".mui", ".winmd", ".lib", ".pdb", ".so", ".node", ".pyd", ".exp", ".ilk", ".idb", ".ipdb", ".iobj", ".obj", ".o", ".a", ".vst3", ".vst", ".clap", ".aax", ".dylib");
        Ext("Pacchetto", "Pacchetto o modulo di programma", ".nupkg", ".jar", ".war", ".aar", ".whl", ".egg", ".gem", ".crate", ".deb", ".rpm", ".apk", ".aab", ".ipa", ".xpi", ".crx", ".vsix", ".unitypackage", ".pkg", ".snap", ".flatpak", ".pack");
        Ext("Database", "File di database", ".db", ".sqlite", ".sqlite3", ".db3", ".mdb", ".accdb", ".mdf", ".ldf", ".ndf", ".ibd", ".frm", ".myd", ".myi", ".dbf", ".ldb", ".sdf", ".fdb", ".realm", ".edb", ".jrs", ".sst", ".ldb.bak", ".idx", ".vlog", ".mmdb", ".kdbx", ".rocksdb", ".leveldb", ".pdc");
        Ext("Codice sorgente", "File di codice sorgente", ".c", ".cpp", ".cc", ".h", ".hpp", ".cs", ".java", ".kt", ".py", ".js", ".tsx", ".jsx", ".go", ".rs", ".rb", ".php", ".swift", ".m", ".mm", ".vb", ".fs", ".dart", ".lua", ".sh", ".ps1", ".bat", ".cmd", ".html", ".htm", ".css", ".scss", ".less", ".xaml", ".json", ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf", ".sql", ".gradle", ".cmake", ".mk", ".map", ".mjs", ".cjs", ".vue", ".svelte", ".astro", ".graphql", ".proto");
        Ext("Font", "Tipo di carattere", ".ttf", ".otf", ".ttc", ".woff", ".woff2", ".fon", ".pfb", ".pfm");
        Ext("Backup", "Copia di sicurezza", ".bak", ".old", ".backup", ".bkp", ".bkf", ".tib", ".tibx", ".vbk", ".vib", ".mrimg", ".spf", ".spi", ".sna", ".ghost", ".gho", ".wbcat", ".ova", ".ovf");
        Ext("File temporaneo", "File temporaneo o parziale", ".tmp", ".temp", ".part", ".crdownload", ".partial", ".download", ".~tmp", ".dtapart", ".!ut", ".bc!", ".swp", ".swo", ".lock");
        Ext("Modello di IA / dati ML", "Modello o pesi di machine learning", ".gguf", ".safetensors", ".ckpt", ".pt", ".pth", ".onnx", ".h5", ".tflite", ".pb", ".ggml", ".npz", ".npy", ".pkl", ".joblib", ".parquet", ".arrow", ".feather", ".msgpack");
        Ext("Gioco (risorse)", "Risorsa o archivio di gioco", ".pak", ".vpk", ".gcf", ".ncf", ".bsa", ".ba2", ".big", ".wad", ".pk3", ".pk4", ".forge", ".arc", ".uasset", ".umap", ".utoc", ".ucas", ".bundle", ".assets", ".resource", ".ress", ".casc", ".cache", ".archive", ".unity3d", ".xpak", ".obw", ".rpf", ".psarc", ".bnk", ".wem", ".fsb", ".bank", ".toc", ".sabs", ".sabl", ".pck", ".vfs", ".hpk", ".pac", ".paz", ".ttarch2");
        Ext("Macchina virtuale (config)", "File di configurazione/stato di macchina virtuale", ".vmx", ".vmsn", ".vmss", ".vmem", ".nvram", ".vbox", ".sav", ".vsv", ".vmrs", ".vmcx", ".vmgs");
        Ext("Sistema", "File di sistema", ".sys", ".efi", ".mof", ".cat", ".inf", ".manifest", ".mum", ".mun", ".pri", ".nls", ".acm", ".ax", ".tlb", ".msc", ".msp", ".msu", ".psf", ".pfx", ".cer", ".p7b", ".rll", ".dat", ".pf", ".sdb");
        Ext("Contenitore Docker/OCI", "Livello o immagine di contenitore", ".tar.zst", ".oci");
        Ext("Sottotitoli / testo", "Sottotitoli o testo semplice", ".srt", ".sub", ".ass", ".vtt", ".nfo", ".lrc");
        Ext("Collegamento", "Collegamento a un altro file", ".lnk", ".url", ".appref-ms");
    }

    private static ushort T(string label, string detail)
    {
        var t = new FileType { Id = (ushort)Types.Count, Label = label, Detail = detail };
        Types.Add(t);
        return t.Id;
    }

    public static FileType GetType(ushort id) => id < Types.Count ? Types[id] : Types[0];
    public static IReadOnlyList<FileType> AllTypes => Types;

    /// <summary>"Cos'è" di un file: la descrizione del tipo, con l'estensione quando il tipo non è riconosciuto.</summary>
    public static string Describe(FileEntry f)
    {
        var t = GetType(f.TypeId);
        if (f.TypeId == TypeUnknown)
        {
            string ext = f.Extension;
            return ext.Length > 0 ? $"File {ext} (tipo non riconosciuto)" : t.Detail;
        }
        return t.Detail;
    }

    public static ushort TypeOf(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        if (dot <= 0 || dot >= fileName.Length - 1) return TypeNoExt;
        // estensioni doppie tipo .tar.gz
        int dot2 = fileName.LastIndexOf('.', dot - 1);
        if (dot2 > 0)
        {
            var e2 = fileName.AsSpan(dot2).ToString().ToLowerInvariant();
            if (ExtToType.TryGetValue(e2, out ushort t2)) return t2;
        }
        var e = fileName.AsSpan(dot).ToString().ToLowerInvariant();
        return ExtToType.TryGetValue(e, out ushort t) ? t : TypeUnknown;
    }

    // ------------------------------------------------------------------ costruzione regole
    private static Rule R(string path, byte cat, byte extra) => new(path.Split('/'), cat, extra);

    private static Rule[] Build(params Rule[] rules) => rules;

    // Per ogni profondità, prima le regole senza wildcard (più specifiche): così "users/public" batte "users/*"
    // qualunque sia l'ordine di dichiarazione. OrderBy è stabile: a parità resta l'ordine scritto. Primo match vince.
    private static readonly Dictionary<int, Rule[]> RulesByDepth = RootRules
        .GroupBy(r => r.Segs.Length)
        .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Segs.Count(s => s.Contains('*'))).ToArray());

    private static bool SegMatch(string pattern, string seg)
    {
        if (pattern.Length == 1 && pattern[0] == '*') return true;
        if (pattern.EndsWith('*')) return seg.AsSpan().StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal);
        return string.Equals(pattern, seg, StringComparison.Ordinal);
    }

    // Contesti in cui scattano le regole "ovunque":
    //  - specifiche (node_modules, .git, __pycache__...): profilo utente, AppData, cartelle libere, SDK, build
    //  - generiche (build, bin, dist, out, release, x64, target, packages): SOLO progetti dell'utente e cartelle
    //    fuori standard -- mai in Documenti/Video ("Release" potrebbe essere la cartella dei render)
    //  - cache/log: come le specifiche, più app dello Store e programmi per utente
    private static readonly HashSet<byte> FreeContexts = [Other, UserOther, AppData, Downloads, DevSdk, DevBuild, Personal, OneDrive];
    private static readonly HashSet<byte> LooseContexts = [Other, UserOther, Downloads];
    // Cache/log "ovunque": MAI in Documenti/Desktop (una cartella "Logs" del diario di volo non è spazzatura)
    private static readonly HashSet<byte> CacheContexts = [Other, UserOther, AppData, Downloads, DevSdk, DevBuild, StoreApps, UserPrograms];
    // node_modules e ambienti virtuali: solo nei progetti dell'utente. MAI in AppData/DevSdk, dove sono parte dei
    // programmi (Program Files\nodejs\node_modules, %APPDATA%\npm\node_modules, <app>\resources\app\node_modules,
    // .vscode\extensions\*\node_modules, %APPDATA%\pypoetry\venv).
    private static readonly HashSet<string> ProjectOnlyAnywhere = new(StringComparer.Ordinal)
        { "node_modules", "node_modules.bak", ".venv", "venv" };
    private static readonly HashSet<byte> ProjectContexts = [Other, UserOther, Downloads, DevBuild, Personal, OneDrive];
    // Le immagini disco (.iso/.img/.wim/.esd) sono "eliminabili" solo dove stanno per caso: cartelle fuori standard,
    // profilo, Download, Documenti/Desktop, temp. Dentro programmi, giochi, SDK, AppData e cartelle VM fanno parte
    // dell'installazione (system.img dell'emulatore Android, gta3.img, VBoxGuestAdditions.iso, winpe.wim).
    private static readonly HashSet<byte> DiskImageContexts = [Other, UserOther, Personal, Downloads, OneDrive, WinTemp, UserTemp];
    // Contesti in cui un .dmp/.log è davvero "log e dump": sistema e applicazioni, non i documenti dell'utente
    // (un dump Oracle o un log di lavoro in Documenti resta della sua categoria).
    private static readonly HashSet<byte> LogContexts = [WindowsSys, AppData, UserOther, Other, Programs, UserPrograms, StoreApps, UserTemp, WinTemp];
    // Cartelle che contengono log di DATABASE vivi (ESE, hive del registro): un .log lì dentro non si tocca.
    private static readonly HashSet<string> DbLogDirs = new(StringComparer.Ordinal)
        { "config", "txr", "apprepository", "unistoredb", "catroot2", "database", "ntds", "dhcp", "wins", "search", "data" };

    /// <summary>
    /// Classifica una cartella appena scoperta. Va chiamato dopo aver impostato Parent/Depth/Name.
    /// Eredita dal genitore salvo regole più specifiche.
    /// </summary>
    public static void ClassifyDir(DirNode node)
    {
        if (node.Parent is null)
        {
            // radice della scansione: se non è la radice del volume, classifica il percorso base per intero
            node.Cat = Other; node.AnchorDepth = 0; node.GroupExtra = 1;
            if (node.RootBaseSegments is { Length: > 0 } segs)
            {
                byte cat = Other, anchor = 0, extra = 1;
                for (int d = 1; d <= segs.Length; d++)
                {
                    bool matched = false;
                    if (RulesByDepth.TryGetValue(d, out var rules))
                    {
                        foreach (var r in rules)
                        {
                            if (MatchPrefix(r.Segs, segs)) { cat = r.Cat; anchor = (byte)d; extra = r.GroupExtra; matched = true; break; }
                        }
                    }
                    if (!matched) ApplyAnywhere(segs[d - 1], ref cat, ref anchor, ref extra, d);
                }
                node.Cat = cat; node.AnchorDepth = anchor; node.GroupExtra = extra;
            }
            return;
        }

        var p = node.Parent;
        node.Cat = p.Cat; node.AnchorDepth = p.AnchorDepth; node.GroupExtra = p.GroupExtra;

        int depth = node.Depth;
        string nameLower = node.Name.ToLowerInvariant();
        if (RulesByDepth.TryGetValue(depth, out var candidates))
        {
            string[]? segs = null;
            foreach (var r in candidates)
            {
                // confronto veloce sull'ultimo segmento prima di costruire tutto il percorso
                var last = r.Segs[^1];
                if (!SegMatch(last, nameLower)) continue;
                segs ??= node.VolumeSegmentsLower();
                if (MatchPrefix(r.Segs, segs))
                {
                    node.Cat = r.Cat; node.AnchorDepth = (byte)depth; node.GroupExtra = r.GroupExtra;
                    return; // la regola ancorata vince
                }
            }
        }

        byte c = node.Cat, a = node.AnchorDepth, e = node.GroupExtra;
        ApplyAnywhere(nameLower, ref c, ref a, ref e, depth);
        node.Cat = c; node.AnchorDepth = a; node.GroupExtra = e;
    }

    private static void ApplyAnywhere(string nameLower, ref byte cat, ref byte anchor, ref byte extra, int depth)
    {
        if (!AnywhereRules.TryGetValue(nameLower, out var rule)) return;
        bool projectOnly = ProjectOnlyAnywhere.Contains(nameLower);
        bool specific = SpecificAnywhere.Contains(nameLower);
        bool generic = GenericBuildNames.Contains(nameLower);
        // Categorie in cui la regola può scattare
        bool ok = projectOnly ? ProjectContexts.Contains(cat)
                : specific ? FreeContexts.Contains(cat)
                : generic ? LooseContexts.Contains(cat)
                : CacheContexts.Contains(cat);
        if (!ok) return;
        cat = rule.Cat; anchor = (byte)depth; extra = rule.Extra;
    }

    private static bool MatchPrefix(string[] pattern, string[] segs)
    {
        if (pattern.Length > segs.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
            if (!SegMatch(pattern[i], segs[i])) return false;
        return true;
    }

    /// <summary>Categoria di un file: eredita dalla cartella, con eccezioni per file speciali ed estensioni.</summary>
    public static byte ClassifyFile(DirNode dir, string name, ushort typeId)
    {
        byte cat = dir.Cat;
        if (dir.Depth == 0 && (dir.Flags & DirFlags.VolumeRoot) != 0)
        {
            string n = name.ToLowerInvariant();
            switch (n)
            {
                case "pagefile.sys": return PageFile;
                case "hiberfil.sys": return Hiberfil;
                case "swapfile.sys": return SwapFile;
                case "dumpstack.log.tmp": return Logs;
                case "bootmgr": case "bootnxt": case "boottel.dat": return WindowsSys;
            }
        }
        if (typeId == TypeVirtualDisk) return VirtualDisks;
        if (typeId == TypeMail) return Mail;
        if (typeId == TypeDump && LogContexts.Contains(cat)) return Logs;
        if (typeId == TypeDiskImage && DiskImageContexts.Contains(cat)) return DiskImages;
        if (typeId == TypeLog && LogContexts.Contains(cat) && !DbLogDirs.Contains(dir.Name.ToLowerInvariant())) return Logs;
        if (typeId == TypeArchive && (cat == Other || cat == UserOther || cat == Personal)) return Archives;
        return cat;
    }
}

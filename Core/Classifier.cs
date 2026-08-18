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
    /// <summary>Chiave nelle tabelle delle stringhe: "cat.&lt;chiave&gt;.name/.desc/.action".</summary>
    public required string Key { get; init; }
    public required Family Family { get; init; }
    public required Safety Safety { get; init; }

    public string Name => Loc.S($"cat.{Key}.name");
    /// <summary>Cos'è.</summary>
    public string Description => Loc.S($"cat.{Key}.desc");
    /// <summary>Come si libera spazio.</summary>
    public string Action => Loc.S($"cat.{Key}.action");
    public override string ToString() => Name;
}

public sealed class FileType
{
    public required ushort Id { get; init; }
    /// <summary>Chiave nelle tabelle delle stringhe: "type.&lt;chiave&gt;.label/.detail".</summary>
    public required string Key { get; init; }

    public string Label => Loc.S($"type.{Key}.label");
    public string Detail => Loc.S($"type.{Key}.detail");
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

    // Nome, descrizione ("cos'è") e azione ("come liberare") stanno nelle tabelle delle stringhe,
    // sotto le chiavi cat.<chiave>.name / .desc / .action (Core\Strings.It.cs e Strings.En.cs).
    public static readonly Category[] Categories =
    [
        C(Other, "other", Family.Other, Safety.Review),
        C(WindowsSys, "windowsSys", Family.Windows, Safety.Keep),
        C(WinSxS, "winSxS", Family.Windows, Safety.Keep),
        C(WinUpdate, "winUpdate", Family.Windows, Safety.Review),
        C(WinInstaller, "winInstaller", Family.Windows, Safety.Keep),
        C(WinTemp, "winTemp", Family.Windows, Safety.Deletable),
        C(Logs, "logs", Family.Windows, Safety.Deletable),
        C(Drivers, "drivers", Family.Windows, Safety.Keep),
        C(WindowsOld, "windowsOld", Family.Windows, Safety.Deletable),
        C(PageFile, "pageFile", Family.System, Safety.Keep),
        C(Hiberfil, "hiberfil", Family.System, Safety.Review),
        C(SwapFile, "swapFile", Family.System, Safety.Keep),
        C(RecycleBin, "recycleBin", Family.System, Safety.Deletable),
        C(RestorePoints, "restorePoints", Family.System, Safety.Review),
        C(Programs, "programs", Family.Programs, Safety.Review),
        C(UserPrograms, "userPrograms", Family.Programs, Safety.Review),
        C(StoreApps, "storeApps", Family.Programs, Safety.Review),
        C(InstallerCache, "installerCache", Family.Programs, Safety.Review),
        C(Games, "games", Family.Programs, Safety.Review),
        C(AppData, "appData", Family.AppData, Safety.Review),
        C(Caches, "caches", Family.AppData, Safety.Deletable),
        C(UserTemp, "userTemp", Family.AppData, Safety.Deletable),
        C(DevCaches, "devCaches", Family.Dev, Safety.Deletable),
        C(DevBuild, "devBuild", Family.Dev, Safety.Deletable),
        C(GitRepos, "gitRepos", Family.Dev, Safety.Review),
        C(DevSdk, "devSdk", Family.Dev, Safety.Review),
        C(VirtualDisks, "virtualDisks", Family.VirtualDisks, Safety.Review),
        C(DiskImages, "diskImages", Family.VirtualDisks, Safety.Deletable),
        C(Downloads, "downloads", Family.Personal, Safety.Deletable),
        C(Personal, "personal", Family.Personal, Safety.Review),
        C(OneDrive, "oneDrive", Family.Personal, Safety.Review),
        C(UserOther, "userOther", Family.Personal, Safety.Review),
        C(Mail, "mail", Family.AppData, Safety.Review),
        C(Archives, "archives", Family.Other, Safety.Review),
        C(SearchIndex, "searchIndex", Family.Windows, Safety.Keep),
        C(Fonts, "fonts", Family.Windows, Safety.Keep),
    ];

    private static Category C(byte id, string key, Family fam, Safety s) =>
        new() { Id = id, Key = key, Family = fam, Safety = s };

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
        TypeUnknown = T("unknown");
        TypeNoExt = T("noext");
        void Ext(string key, params string[] exts)
        {
            ushort id = T(key);
            foreach (var e in exts)
            {
                // Una chiave doppia sovrascriverebbe in silenzio: meglio saperlo subito.
                if (!ExtToType.TryAdd(e, id)) throw new InvalidOperationException($"Estensione dichiarata due volte nella tabella dei tipi: {e}");
            }
        }
        TypeVirtualDisk = T("virtualdisk");
        foreach (var e in new[] { ".vhdx", ".vhd", ".avhdx", ".vmdk", ".vdi", ".qcow2", ".hdd", ".vbox-prev" }) ExtToType[e] = TypeVirtualDisk;
        TypeDiskImage = T("diskimage");
        foreach (var e in new[] { ".iso", ".img", ".wim", ".esd", ".swm", ".dmg", ".nrg", ".mds", ".cue" }) ExtToType[e] = TypeDiskImage;
        TypeMail = T("mail");
        foreach (var e in new[] { ".pst", ".ost" }) ExtToType[e] = TypeMail;
        TypeArchive = T("archive");
        foreach (var e in new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".cab", ".arj", ".lz", ".lzma", ".z" }) ExtToType[e] = TypeArchive;
        TypeLog = T("log");
        foreach (var e in new[] { ".log", ".etl", ".evtx", ".txt.log", ".trace" }) ExtToType[e] = TypeLog;
        TypeDump = T("dump");
        foreach (var e in new[] { ".dmp", ".mdmp", ".hdmp", ".kdmp" }) ExtToType[e] = TypeDump;
        // Hive del registro e loro log transazionali: NON sono log di testo e non vanno toccati.
        TypeRegistry = T("registry");
        foreach (var e in new[] { ".hve", ".hiv", ".log1", ".log2", ".blf", ".regtrans-ms" }) ExtToType[e] = TypeRegistry;

        // NB: ogni estensione va dichiarata UNA volta sola (Ext lancia se doppia). ".ts" è video, non TypeScript:
        // per un analizzatore di spazio conta chi pesa. ".dat" e ".idx" sono generici e stanno in "Sistema"/"Database".
        Ext("video", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".mpg", ".mpeg", ".webm", ".flv", ".ts", ".m2ts", ".mts", ".3gp", ".vob", ".ogv", ".divx", ".mxf", ".braw", ".r3d");
        Ext("audio", ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma", ".aif", ".aiff", ".opus", ".alac", ".ape", ".dsf", ".mid", ".midi", ".wv", ".caf");
        Ext("audioproject", ".als", ".flp", ".cpr", ".logicx", ".ptx", ".rpp", ".song", ".reason", ".band", ".sesx", ".dawproject");
        Ext("samples", ".nki", ".nkx", ".nkc", ".nkm", ".nkr", ".ncw", ".sf2", ".sfz", ".exs", ".kontakt", ".nicnt", ".soundbank", ".ufs", ".upl", ".vsn", ".rex", ".rx2");
        Ext("image", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".psd", ".psb", ".ai", ".svg", ".ico", ".xcf", ".kra", ".avif", ".jxl", ".exr", ".hdr", ".tga");
        Ext("document", ".pdf", ".doc", ".docx", ".odt", ".rtf", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp", ".pages", ".numbers", ".key", ".epub", ".mobi", ".azw3", ".djvu", ".one", ".xps", ".pub", ".md", ".txt", ".csv", ".tsv");
        Ext("program", ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".appinstaller", ".com", ".scr");
        Ext("library", ".dll", ".ocx", ".drv", ".cpl", ".mui", ".winmd", ".lib", ".pdb", ".so", ".node", ".pyd", ".exp", ".ilk", ".idb", ".ipdb", ".iobj", ".obj", ".o", ".a", ".vst3", ".vst", ".clap", ".aax", ".dylib");
        Ext("package", ".nupkg", ".jar", ".war", ".aar", ".whl", ".egg", ".gem", ".crate", ".deb", ".rpm", ".apk", ".aab", ".ipa", ".xpi", ".crx", ".vsix", ".unitypackage", ".pkg", ".snap", ".flatpak", ".pack");
        Ext("database", ".db", ".sqlite", ".sqlite3", ".db3", ".mdb", ".accdb", ".mdf", ".ldf", ".ndf", ".ibd", ".frm", ".myd", ".myi", ".dbf", ".ldb", ".sdf", ".fdb", ".realm", ".edb", ".jrs", ".sst", ".ldb.bak", ".idx", ".vlog", ".mmdb", ".kdbx", ".rocksdb", ".leveldb", ".pdc");
        Ext("source", ".c", ".cpp", ".cc", ".h", ".hpp", ".cs", ".java", ".kt", ".py", ".js", ".tsx", ".jsx", ".go", ".rs", ".rb", ".php", ".swift", ".m", ".mm", ".vb", ".fs", ".dart", ".lua", ".sh", ".ps1", ".bat", ".cmd", ".html", ".htm", ".css", ".scss", ".less", ".xaml", ".json", ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf", ".sql", ".gradle", ".cmake", ".mk", ".map", ".mjs", ".cjs", ".vue", ".svelte", ".astro", ".graphql", ".proto");
        Ext("font", ".ttf", ".otf", ".ttc", ".woff", ".woff2", ".fon", ".pfb", ".pfm");
        Ext("backup", ".bak", ".old", ".backup", ".bkp", ".bkf", ".tib", ".tibx", ".vbk", ".vib", ".mrimg", ".spf", ".spi", ".sna", ".ghost", ".gho", ".wbcat", ".ova", ".ovf");
        Ext("temp", ".tmp", ".temp", ".part", ".crdownload", ".partial", ".download", ".~tmp", ".dtapart", ".!ut", ".bc!", ".swp", ".swo", ".lock");
        Ext("ml", ".gguf", ".safetensors", ".ckpt", ".pt", ".pth", ".onnx", ".h5", ".tflite", ".pb", ".ggml", ".npz", ".npy", ".pkl", ".joblib", ".parquet", ".arrow", ".feather", ".msgpack");
        Ext("game", ".pak", ".vpk", ".gcf", ".ncf", ".bsa", ".ba2", ".big", ".wad", ".pk3", ".pk4", ".forge", ".arc", ".uasset", ".umap", ".utoc", ".ucas", ".bundle", ".assets", ".resource", ".ress", ".casc", ".cache", ".archive", ".unity3d", ".xpak", ".obw", ".rpf", ".psarc", ".bnk", ".wem", ".fsb", ".bank", ".toc", ".sabs", ".sabl", ".pck", ".vfs", ".hpk", ".pac", ".paz", ".ttarch2");
        Ext("vmconfig", ".vmx", ".vmsn", ".vmss", ".vmem", ".nvram", ".vbox", ".sav", ".vsv", ".vmrs", ".vmcx", ".vmgs");
        Ext("system", ".sys", ".efi", ".mof", ".cat", ".inf", ".manifest", ".mum", ".mun", ".pri", ".nls", ".acm", ".ax", ".tlb", ".msc", ".msp", ".msu", ".psf", ".pfx", ".cer", ".p7b", ".rll", ".dat", ".pf", ".sdb");
        Ext("container", ".tar.zst", ".oci");
        Ext("subtitles", ".srt", ".sub", ".ass", ".vtt", ".nfo", ".lrc");
        Ext("shortcut", ".lnk", ".url", ".appref-ms");
    }

    private static ushort T(string key)
    {
        var t = new FileType { Id = (ushort)Types.Count, Key = key };
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
            return ext.Length > 0 ? Loc.S("type.unknown.ext", ext) : t.Detail;
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

using System.Globalization;
using System.Text.Json;

namespace Sonda.Core;

public enum Lang : byte
{
    /// <summary>Segue la lingua di Windows: italiano se il sistema è italiano, altrimenti inglese.</summary>
    Auto = 0,
    Italiano = 1,
    English = 2,
}

/// <summary>
/// Stringhe dell'interfaccia in italiano e inglese.
///
/// Non si usano i file .resx: con la pubblicazione a file singolo le risorse satellite complicano il
/// pacchetto, e qui le lingue sono due e le chiavi poche centinaia. Un dizionario per lingua basta,
/// si legge tutto in un posto solo ed è facile aggiungerne una terza (basta un altro file come Strings.en.cs).
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _map = Strings.It;
    private static Lang _setting = Lang.Auto;

    /// <summary>Impostazione scelta dall'utente (può essere Auto).</summary>
    public static Lang Setting => _setting;

    /// <summary>Lingua effettivamente in uso (mai Auto).</summary>
    public static Lang Current { get; private set; } = Lang.Italiano;

    public static bool IsItalian => Current == Lang.Italiano;

    /// <summary>Cultura da usare per numeri e date.</summary>
    public static CultureInfo Culture => Current == Lang.Italiano
        ? CultureInfo.GetCultureInfo("it-IT")
        : CultureInfo.GetCultureInfo("en-US");

    /// <summary>Lingua che verrebbe scelta automaticamente in base a Windows.</summary>
    public static Lang SystemLanguage =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.Equals("it", StringComparison.OrdinalIgnoreCase)
        || CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("it", StringComparison.OrdinalIgnoreCase)
            ? Lang.Italiano : Lang.English;

    public static void Apply(Lang lang)
    {
        _setting = lang;
        Current = lang == Lang.Auto ? SystemLanguage : lang;
        _map = Current == Lang.Italiano ? Strings.It : Strings.En;
        var c = Culture;
        CultureInfo.DefaultThreadCurrentCulture = c;
        CultureInfo.DefaultThreadCurrentUICulture = c;
        Thread.CurrentThread.CurrentCulture = c;
        Thread.CurrentThread.CurrentUICulture = c;
    }

    /// <summary>Testo della chiave. Se manca, ripiega sull'italiano e infine sulla chiave stessa.</summary>
    public static string S(string key)
    {
        if (_map.TryGetValue(key, out var s)) return s;
        if (Strings.It.TryGetValue(key, out s)) return s;
        return key;
    }

    public static string S(string key, params object[] args)
    {
        string f = S(key);
        try { return string.Format(Culture, f, args); }
        catch (FormatException) { return f; }
    }

    // ------------------------------------------------------------------ impostazioni su disco
    private sealed class SettingsFile
    {
        public string? Lingua { get; set; }
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sonda", "impostazioni.json");

    /// <summary>Legge l'impostazione salvata e la applica. Da chiamare all'avvio.</summary>
    public static void LoadSettings()
    {
        var lang = Lang.Auto;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(SettingsPath));
                if (s?.Lingua is string v && Enum.TryParse<Lang>(v, ignoreCase: true, out var parsed)) lang = parsed;
            }
        }
        catch { /* impostazioni illeggibili: si riparte da Auto */ }
        Apply(lang);
    }

    /// <summary>Salva la scelta e la applica subito.</summary>
    public static void SaveSettings(Lang lang)
    {
        Apply(lang);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new SettingsFile { Lingua = lang.ToString() },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* se non si può scrivere, la scelta vale solo per questa sessione */ }
    }

    /// <summary>Nome della lingua da mostrare nell'elenco delle impostazioni.</summary>
    public static string LangName(Lang lang) => lang switch
    {
        Lang.Italiano => "Italiano",
        Lang.English => "English",
        _ => S("settings.lang.auto") + $" ({(SystemLanguage == Lang.Italiano ? "Italiano" : "English")})",
    };
}

namespace Sonda.Core;

public static class Format
{
    /// <summary>"12,3 GB", "845 MB", "3,2 KB". Base 1024 come Esplora risorse.</summary>
    public static string Bytes(long b)
    {
        if (b < 0) return "-" + Bytes(-b);
        if (b < 1024) return b.ToString("N0", Loc.Culture) + " " + Loc.S("fmt.bytes");
        double v = b / 1024.0;
        string[] u = ["KB", "MB", "GB", "TB"];
        int i = 0;
        // 1023,6 KB arrotonderebbe a "1.024 KB": si passa all'unità sopra quando l'arrotondamento la raggiunge
        while (v >= 1023.5 && i < u.Length - 1) { v /= 1024; i++; }
        var c = Loc.Culture;
        string s = v >= 99.95 ? v.ToString("N0", c) : v >= 9.995 ? v.ToString("N1", c) : v.ToString("N2", c);
        return s + " " + u[i];
    }

    public static string BytesShort(long b)
    {
        if (b < 1024) return b + " B";
        double v = b / 1024.0;
        string[] u = ["K", "M", "G", "T"];
        int i = 0;
        while (v >= 1023.5 && i < u.Length - 1) { v /= 1024; i++; }
        var c = Loc.Culture;
        return (v >= 9.95 ? v.ToString("N0", c) : v.ToString("N1", c)) + u[i];
    }

    public static string Percent(double p)
    {
        var c = Loc.Culture;
        if (p < 0.0005) return "<" + 0.1.ToString("N1", c) + "%";
        return p < 0.1 ? (p * 100).ToString("N1", c) + "%" : (p * 100).ToString("N0", c) + "%";
    }

    public static string Count(long n) => n.ToString("N0", Loc.Culture);

    public static string Date(DateTime d) => d == DateTime.MinValue
        ? ""
        : d.ToString(Loc.IsItalian ? "dd/MM/yyyy HH:mm" : "yyyy-MM-dd HH:mm", Loc.Culture);

    public static string Duration(TimeSpan t) => t.TotalSeconds < 60
        ? t.TotalSeconds.ToString("0.0", Loc.Culture) + " " + Loc.S("fmt.seconds")
        : $"{(int)t.TotalMinutes} {Loc.S("fmt.minutes")} {t.Seconds} {Loc.S("fmt.seconds")}";

    public static string SafetyLabel(Safety s) => s switch
    {
        Safety.Deletable => Loc.S("safety.deletable"),
        Safety.Review => Loc.S("safety.review"),
        _ => Loc.S("safety.keep"),
    };
}

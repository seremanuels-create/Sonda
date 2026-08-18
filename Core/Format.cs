using System.Globalization;

namespace Sonda.Core;

public static class Format
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

    /// <summary>"12,3 GB", "845 MB", "3,2 KB". Base 1024 come Esplora risorse.</summary>
    public static string Bytes(long b)
    {
        if (b < 0) return "-" + Bytes(-b);
        if (b < 1024) return b.ToString("N0", It) + " byte";
        double v = b / 1024.0;
        string[] u = ["KB", "MB", "GB", "TB"];
        int i = 0;
        // 1023,6 KB arrotonderebbe a "1.024 KB": si passa all'unità sopra quando l'arrotondamento la raggiunge
        while (v >= 1023.5 && i < u.Length - 1) { v /= 1024; i++; }
        string s = v >= 99.95 ? v.ToString("N0", It) : v >= 9.995 ? v.ToString("N1", It) : v.ToString("N2", It);
        return s + " " + u[i];
    }

    public static string BytesShort(long b)
    {
        if (b < 1024) return b + " B";
        double v = b / 1024.0;
        string[] u = ["K", "M", "G", "T"];
        int i = 0;
        while (v >= 1023.5 && i < u.Length - 1) { v /= 1024; i++; }
        return (v >= 9.95 ? v.ToString("N0", It) : v.ToString("N1", It)) + u[i];
    }

    public static string Percent(double p)
    {
        if (p < 0.0005) return "<0,1%";
        return p < 0.1 ? (p * 100).ToString("N1", It) + "%" : (p * 100).ToString("N0", It) + "%";
    }

    public static string Count(long n) => n.ToString("N0", It);

    public static string Date(DateTime d) => d == DateTime.MinValue ? "" : d.ToString("dd/MM/yyyy HH:mm", It);

    public static string Duration(TimeSpan t) => t.TotalSeconds < 60 ? $"{t.TotalSeconds:0.0} s" : $"{(int)t.TotalMinutes} min {t.Seconds} s";

    public static string SafetyLabel(Safety s) => s switch
    {
        Safety.Deletable => "Eliminabile",
        Safety.Review => "Da valutare",
        _ => "Non toccare",
    };

    public static string SafetyGlyph(Safety s) => s switch
    {
        Safety.Deletable => "●",
        Safety.Review => "●",
        _ => "●",
    };
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Sonda.Core;

namespace Sonda.UI;

public sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is long b ? Format.Bytes(b) : value is int i ? Format.Bytes(i) : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is double d ? Format.Percent(d) : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class CountConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is long l ? Format.Count(l) : value is int i ? Format.Count(i) : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class DateConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is DateTime d ? Format.Date(d) : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Quota (0..1) → larghezza in pixel della barretta. Parametro = larghezza massima.</summary>
public sealed class ShareWidthConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        double max = p is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 100;
        double share = value is double d ? d : 0;
        if (double.IsNaN(share) || share < 0) share = 0;
        if (share > 1) share = 1;
        double w = share * max;
        return w > 0 && w < 2 ? 2 : w;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public static class Palette
{
    // Colori di famiglia (tavolozza categoriale validata: blu, arancio, acqua, giallo, magenta, verde, viola, rosso + grigio)
    public static readonly Dictionary<Family, Color> FamilyColors = new()
    {
        [Family.Windows] = (Color)ColorConverter.ConvertFromString("#2A78D6"),
        [Family.AppData] = (Color)ColorConverter.ConvertFromString("#EB6834"),
        [Family.Personal] = (Color)ColorConverter.ConvertFromString("#1BAF7A"),
        [Family.System] = (Color)ColorConverter.ConvertFromString("#EDA100"),
        [Family.VirtualDisks] = (Color)ColorConverter.ConvertFromString("#E87BA4"),
        [Family.Dev] = (Color)ColorConverter.ConvertFromString("#008300"),
        [Family.Programs] = (Color)ColorConverter.ConvertFromString("#4A3AA7"),
        [Family.Other] = (Color)ColorConverter.ConvertFromString("#898781"),
    };
    private static readonly Dictionary<Family, SolidColorBrush> Brushes = FamilyColors.ToDictionary(kv => kv.Key, kv =>
    {
        var b = new SolidColorBrush(kv.Value); b.Freeze(); return b;
    });
    public static SolidColorBrush Brush(Family f) => Brushes.TryGetValue(f, out var b) ? b : Brushes[Family.Other];
    public static Color Color(Family f) => FamilyColors.TryGetValue(f, out var c) ? c : FamilyColors[Family.Other];

    public static string FamilyName(Family f) => f switch
    {
        Family.Windows => Loc.S("family.windows"),
        Family.Programs => Loc.S("family.programs"),
        Family.AppData => Loc.S("family.appdata"),
        Family.Personal => Loc.S("family.personal"),
        Family.Dev => Loc.S("family.dev"),
        Family.System => Loc.S("family.system"),
        Family.VirtualDisks => Loc.S("family.virtualdisks"),
        _ => Loc.S("family.other"),
    };

    public static SolidColorBrush Safety(Safety s)
    {
        var app = Application.Current;
        string key = s switch { Core.Safety.Deletable => "SafeDeletableBrush", Core.Safety.Review => "SafeReviewBrush", _ => "SafeKeepBrush" };
        return (SolidColorBrush)app.Resources[key];
    }
}

public sealed class FamilyBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        Family f => Palette.Brush(f),
        Category cat => Palette.Brush(cat.Family),
        byte id => Palette.Brush(Classifier.Get(id).Family),
        _ => Palette.Brush(Family.Other),
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class SafetyBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        Safety s => Palette.Safety(s),
        Category cat => Palette.Safety(cat.Safety),
        _ => Palette.Safety(Safety.Review),
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class SafetyLabelConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        Safety s => Format.SafetyLabel(s),
        Category cat => Format.SafetyLabel(cat.Safety),
        _ => "",
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InvBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NullToVisConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

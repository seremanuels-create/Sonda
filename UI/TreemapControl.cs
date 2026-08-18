using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sonda.UI;

public sealed class TreemapItem
{
    public required string Label { get; init; }
    public required long Weight { get; init; }
    public required Color Color { get; init; }
    public object? Tag { get; init; }
    public bool IsDir { get; init; }
    public string? Tooltip { get; init; }
    public List<TreemapItem>? Children { get; set; }
    internal Rect Rect;
    internal int Depth;
    internal TreemapItem? Parent;
}

/// <summary>
/// Mappa ad albero "squarified": ogni rettangolo è proporzionale allo spazio occupato. Due livelli:
/// le cartelle contengono i loro figli più grandi. Passa il mouse per il dettaglio, clic per selezionare,
/// doppio clic per entrare.
/// </summary>
public sealed class TreemapControl : FrameworkElement
{
    private List<TreemapItem> _items = new();
    private readonly List<TreemapItem> _flat = new();
    private TreemapItem? _hover;
    private TreemapItem? _selected;
    // Tooltip gestito a mano (non tramite ToolTipService, che lo apre una volta sola per elemento):
    // ogni cambio di rettangolo lo chiude e lo riapre accanto al mouse.
    private readonly ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse, StaysOpen = true };

    public event Action<TreemapItem>? ItemClicked;
    public event Action<TreemapItem>? ItemActivated;

    public TreemapControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        _tip.PlacementTarget = this;
    }

    public void SetItems(IEnumerable<TreemapItem> items)
    {
        _items = items.Where(i => i.Weight > 0).OrderByDescending(i => i.Weight).ToList();
        _hover = null; _selected = null; _tip.IsOpen = false;
        InvalidateVisual();
    }

    public void SetSelected(object? tag)
    {
        _selected = _flat.FirstOrDefault(i => Equals(i.Tag, tag) && i.Depth == 0);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF1)), null, bounds);
        _flat.Clear();
        if (_items.Count == 0 || bounds.Width < 4 || bounds.Height < 4)
        {
            var ft = Text(Sonda.Core.Loc.S("ui.treemap.empty"), 13, Color.FromRgb(0x89, 0x87, 0x81));
            dc.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, (bounds.Height - ft.Height) / 2));
            return;
        }
        Layout(_items, bounds.DeflateSafe(1), 0, null);
        foreach (var it in _items) DrawItem(dc, it);
        if (_selected is not null)
            dc.DrawRectangle(null, new Pen(Brushes.Black, 2), _selected.Rect.DeflateSafe(1));
        if (_hover is not null && _hover != _selected)
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 1.5), _hover.Rect.DeflateSafe(0.75));
    }

    private void DrawItem(DrawingContext dc, TreemapItem it)
    {
        var r = it.Rect;
        if (r.Width < 1 || r.Height < 1) return;
        _flat.Add(it);
        Color c = it.Color;
        // cartelle: colore pieno; file: più chiaro. Livelli annidati: ancora più chiari.
        Color fill = it.IsDir ? Lighten(c, 0.10 + 0.18 * it.Depth) : Lighten(c, 0.35 + 0.15 * it.Depth);
        var brush = new SolidColorBrush(fill);
        // 2 px di respiro fra i rettangoli
        var inner = r.DeflateSafe(1);
        dc.DrawRoundedRectangle(brush, null, inner, 2, 2);

        bool drewChildren = false;
        if (it.IsDir && it.Children is { Count: > 0 } && inner.Width >= 60 && inner.Height >= 44 && it.Depth < 1)
        {
            // fascia per l'etichetta in alto, poi i figli
            double band = 18;
            var childRect = new Rect(inner.X + 3, inner.Y + band, Math.Max(0, inner.Width - 6), Math.Max(0, inner.Height - band - 3));
            if (childRect.Width > 8 && childRect.Height > 8)
            {
                var kids = it.Children.Where(k => k.Weight > 0).OrderByDescending(k => k.Weight).ToList();
                // taglia la coda: quello che non entra si accorpa in "altro"
                long total = kids.Sum(k => k.Weight);
                double area = childRect.Width * childRect.Height;
                var keep = new List<TreemapItem>();
                long rest = 0;
                foreach (var k in kids)
                {
                    double a = area * k.Weight / Math.Max(1, total);
                    if (a >= 60 && keep.Count < 40) keep.Add(k); else rest += k.Weight;
                }
                if (rest > 0) keep.Add(new TreemapItem { Label = Sonda.Core.Loc.S("ui.treemap.other"), Weight = rest, Color = it.Color, IsDir = false, Tag = null });
                Layout(keep, childRect, it.Depth + 1, it);
                foreach (var k in keep) DrawItem(dc, k);
                drewChildren = true;
                DrawLabel(dc, it, new Rect(inner.X, inner.Y, inner.Width, band), true);
            }
        }
        if (!drewChildren) DrawLabel(dc, it, inner, false);
    }

    private void DrawLabel(DrawingContext dc, TreemapItem it, Rect r, bool bandOnly)
    {
        if (r.Width < 28 || r.Height < 13) return;
        Color ink = Color.FromRgb(0x0B, 0x0B, 0x0B);
        double pad = 4;
        var name = Text(it.Label, it.Depth == 0 ? 12.5 : 11.5, ink, it.Depth == 0 ? FontWeights.SemiBold : FontWeights.Normal, r.Width - 2 * pad);
        double y = r.Y + 2;
        if (!bandOnly && r.Height >= 34)
        {
            dc.DrawText(name, new Point(r.X + pad, y));
            var size = Text(Sonda.Core.Format.Bytes(it.Weight), 11, Color.FromRgb(0x33, 0x33, 0x33), FontWeights.Normal, r.Width - 2 * pad);
            dc.DrawText(size, new Point(r.X + pad, y + name.Height + 1));
        }
        else
        {
            if (r.Width >= 90 && !bandOnly)
            {
                var one = Text($"{it.Label}  {Sonda.Core.Format.BytesShort(it.Weight)}", 11.5, ink, it.Depth == 0 ? FontWeights.SemiBold : FontWeights.Normal, r.Width - 2 * pad);
                dc.DrawText(one, new Point(r.X + pad, r.Y + (r.Height - one.Height) / 2));
            }
            else if (bandOnly && r.Width >= 60)
            {
                var one = Text($"{it.Label}  {Sonda.Core.Format.BytesShort(it.Weight)}", 11.5, ink, FontWeights.SemiBold, r.Width - 2 * pad);
                dc.DrawText(one, new Point(r.X + pad, r.Y + 1));
            }
            else dc.DrawText(name, new Point(r.X + pad, r.Y + (r.Height - name.Height) / 2));
        }
    }

    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface FaceBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private FormattedText Text(string s, double size, Color color, FontWeight? w = null, double maxWidth = 10000)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            w == FontWeights.SemiBold ? FaceBold : Face, size, new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        return ft;
    }

    private static Color Lighten(Color c, double f)
    {
        f = Math.Clamp(f, 0, 0.9);
        return Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    }

    // ---- squarified layout ----
    private static void Layout(List<TreemapItem> items, Rect rect, int depth, TreemapItem? parent)
    {
        double total = 0;
        foreach (var i in items) { i.Depth = depth; i.Parent = parent; total += i.Weight; }
        if (total <= 0 || rect.Width <= 0 || rect.Height <= 0) { foreach (var i in items) i.Rect = Rect.Empty; return; }
        double scale = rect.Width * rect.Height / total;
        var areas = items.Select(i => i.Weight * scale).ToList();

        int start = 0;
        var cur = rect;
        while (start < items.Count)
        {
            bool horizontal = cur.Width >= cur.Height; // la striscia corre lungo il lato corto
            double side = horizontal ? cur.Height : cur.Width;
            int end = start;
            double rowSum = areas[start];
            double worst = Worst(rowSum, areas[start], areas[start], side);
            end++;
            while (end < items.Count)
            {
                double s2 = rowSum + areas[end];
                double w2 = Worst(s2, MinIn(areas, start, end + 1), MaxIn(areas, start, end + 1), side);
                if (w2 <= worst) { worst = w2; rowSum = s2; end++; }
                else break;
            }
            // disponi la striscia
            double thick = side > 0 ? rowSum / side : 0;
            double pos = horizontal ? cur.Y : cur.X;
            for (int i = start; i < end; i++)
            {
                double len = thick > 0 ? areas[i] / thick : 0;
                items[i].Rect = horizontal
                    ? new Rect(cur.X, pos, thick, len)
                    : new Rect(pos, cur.Y, len, thick);
                pos += len;
            }
            cur = horizontal
                ? new Rect(cur.X + thick, cur.Y, Math.Max(0, cur.Width - thick), cur.Height)
                : new Rect(cur.X, cur.Y + thick, cur.Width, Math.Max(0, cur.Height - thick));
            start = end;
        }
    }

    private static double MinIn(List<double> a, int s, int e) { double m = double.MaxValue; for (int i = s; i < e; i++) m = Math.Min(m, a[i]); return m; }
    private static double MaxIn(List<double> a, int s, int e) { double m = 0; for (int i = s; i < e; i++) m = Math.Max(m, a[i]); return m; }
    private static double Worst(double sum, double min, double max, double side)
    {
        if (sum <= 0 || side <= 0) return double.MaxValue;
        double s2 = sum * sum, w2 = side * side;
        return Math.Max(w2 * max / s2, s2 / (w2 * Math.Max(min, 1e-9)));
    }

    // ---- interazione ----
    private TreemapItem? HitTest(Point p)
    {
        TreemapItem? best = null;
        foreach (var it in _flat)
            if (it.Rect.Contains(p) && (best is null || it.Depth > best.Depth)) best = it;
        return best;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var it = HitTest(e.GetPosition(this));
        if (it != _hover)
        {
            _hover = it;
            _tip.IsOpen = false;
            if (it is not null)
            {
                _tip.Content = it.Tooltip ?? $"{it.Label}\n{Sonda.Core.Format.Bytes(it.Weight)}";
                _tip.IsOpen = true;
            }
            InvalidateVisual();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null; _tip.IsOpen = false; InvalidateVisual();
        base.OnMouseLeave(e);
    }


    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var it = HitTest(e.GetPosition(this));
        if (it is null) return;
        if (e.ClickCount == 2) _tip.IsOpen = false;
        // risali al primo livello per selezione/attivazione (i figli sono solo un'anteprima)
        var top = it; while (top.Parent is not null) top = top.Parent;
        if (e.ClickCount == 2) ItemActivated?.Invoke(top);
        else { _selected = top; InvalidateVisual(); ItemClicked?.Invoke(top); }
        e.Handled = true;
    }
}

internal static class RectExt
{
    public static Rect DeflateSafe(this Rect r, double d)
    {
        double w = r.Width - 2 * d, h = r.Height - 2 * d;
        if (w <= 0 || h <= 0) return new Rect(r.X, r.Y, 0, 0);
        return new Rect(r.X + d, r.Y + d, w, h);
    }
}

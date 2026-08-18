using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Sonda.Core;
using GroupItem = Sonda.Core.GroupItem;

namespace Sonda.UI;

/// <summary>Riga di tabella: una cartella o un file, con tutto quello che serve alle colonne.</summary>
public sealed class Row
{
    public string Name { get; init; } = "";
    public bool IsDir { get; init; }
    public string Glyph => IsDir ? (Node is { IsReparse: true } ? "" : Node is { IsAccessDenied: true } ? "" : "") : "";
    public long OnDisk { get; init; }
    public long Size { get; init; }
    public double Share { get; init; }
    public int Files { get; init; }
    public DateTime Modified { get; init; }
    public string TypeLabel { get; init; } = "";
    public string What { get; init; } = "";
    public string CategoryName { get; init; } = "";
    public Safety Safety { get; init; }
    public Family Family { get; init; }
    public string Path { get; init; } = "";
    public string Folder { get; init; } = "";
    public string Note { get; init; } = "";
    public DirNode? Node { get; init; }
    public FileEntry? File { get; init; }
    public string FilesText => IsDir ? Format.Count(Files) : "";
    public string ShareText => Format.Percent(Share);
    public string OnDiskText => Format.Bytes(OnDisk);
    public string SizeText => Format.Bytes(Size);
    public string ModifiedText => Format.Date(Modified);
    public string Tooltip => IsDir
        ? $"{Path}\n{Format.Bytes(OnDisk)} su disco  ·  {Format.Count(Files)} file\n{CategoryName}{(Note.Length > 0 ? "\n" + Note : "")}"
        : $"{Path}\n{Format.Bytes(OnDisk)} su disco ({Format.Bytes(Size)} logici)\n{What}\n{CategoryName}{(Note.Length > 0 ? "\n" + Note : "")}";

    public static Row FromDir(DirNode d, long parentTotal)
    {
        var cat = Classifier.Get(d.Cat);
        string note = d.IsReparse ? "Giunzione/collegamento: il contenuto è contato altrove"
                    : d.IsAccessDenied ? "Accesso negato: contenuto sconosciuto"
                    : d.HasError ? "Errore: " + d.ErrorMessage : "";
        return new Row
        {
            Name = d.Name, IsDir = true, Node = d,
            OnDisk = d.OnDisk, Size = d.Size, Files = d.FileCount,
            Share = parentTotal > 0 ? (double)d.OnDisk / parentTotal : 0,
            Modified = d.ModifiedTicks > 0 ? new DateTime(d.ModifiedTicks, DateTimeKind.Utc).ToLocalTime() : DateTime.MinValue,
            TypeLabel = d.IsReparse ? "Collegamento" : "Cartella",
            What = d.IsReparse ? "Giunzione o collegamento simbolico" : cat.Description,
            CategoryName = cat.Name, Safety = cat.Safety, Family = cat.Family,
            Path = d.FullPath, Folder = d.Parent?.FullPath ?? "", Note = note,
        };
    }

    public static Row FromFile(FileEntry f, long parentTotal)
    {
        var cat = Classifier.Get(f.Cat);
        var t = Classifier.GetType(f.TypeId);
        string note = f.IsCloudPlaceholder ? "File cloud: occupa solo lo spazio scaricato"
                    : f.IsCompressed ? "Compresso da NTFS"
                    : f.IsSparse ? "File sparse" : "";
        return new Row
        {
            Name = f.Name, IsDir = false, File = f,
            OnDisk = f.OnDisk, Size = f.Size, Files = 0,
            Share = parentTotal > 0 ? (double)f.OnDisk / parentTotal : 0,
            Modified = f.Modified,
            TypeLabel = t.Label, What = Classifier.Describe(f),
            CategoryName = cat.Name, Safety = cat.Safety, Family = cat.Family,
            Path = f.FullPath, Folder = f.Dir.FullPath, Note = note,
        };
    }
}

public sealed class CauseRow
{
    public required Cause Cause { get; init; }
    public int Rank => Cause.Rank;
    public string Name => Cause.Category.Name;
    public long OnDisk => Cause.OnDisk;
    public string OnDiskText => Format.Bytes(Cause.OnDisk);
    public double Share => Cause.Share;
    public string ShareText => Format.Percent(Cause.Share);
    public double BarShare { get; init; }  // rispetto alla causa principale, per la barretta
    public int Files => Cause.Files;
    public string FilesText => Format.Count(Cause.Files) + " file";
    public Family Family => Cause.Category.Family;
    public Safety Safety => Cause.Category.Safety;
    public string Description => Cause.Category.Description;
    public string Action => Cause.Category.Action;
}

public sealed class GroupRow
{
    public required GroupItem Item { get; init; }
    public string Name => Item.Dir.IsRoot ? Item.Dir.Name : Item.Dir.Name;
    public string Path => Item.Path;
    public long OnDisk => Item.OnDisk;
    public string OnDiskText => Format.Bytes(Item.OnDisk);
    public int Files => Item.Files;
    public string FilesText => Format.Count(Item.Files);
    public double Share { get; init; }
    public string ShareText => Format.Percent(Share);
    public string Note => Item.Dir.IsReparse ? "collegamento" : Item.Dir.IsAccessDenied ? "accesso negato" : "";
    /// <summary>Ultimi tre segmenti del percorso: "…\AppData\Local\Google".</summary>
    public string ShortPath
    {
        get
        {
            var p = Item.Path.TrimEnd('\\');
            var parts = p.Split('\\');
            if (parts.Length <= 3) return Item.Path;
            return "…\\" + string.Join('\\', parts.Skip(parts.Length - 3));
        }
    }
}

public sealed class TypeRow
{
    public required TypeStat Stat { get; init; }
    public string Label => Stat.Type.Label;
    public string Detail => Stat.Type.Detail;
    public long OnDisk => Stat.OnDisk;
    public string OnDiskText => Format.Bytes(Stat.OnDisk);
    public double Share => Stat.Share;
    public string ShareText => Format.Percent(Stat.Share);
    public int Files => Stat.Files;
    public string FilesText => Format.Count(Stat.Files);
    public string Extensions => Stat.TopExtensions ?? "";
}

public sealed class BalanceRow
{
    public required BalanceLine Line { get; init; }
    public string Label => Line.Label;
    public string BytesText => Line.Bytes is long b ? Format.Bytes(b) : "—";
    public string Note => Line.Note;
    public bool IsTotal => Line.IsTotal;
    public bool IsWarning => Line.IsWarning;
    public FontWeight Weight => Line.IsTotal ? FontWeights.SemiBold : FontWeights.Normal;
}

/// <summary>Ordinamento per clic sull'intestazione di colonna (GridView).</summary>
public static class GridViewSort
{
    public static readonly DependencyProperty PropertyNameProperty =
        DependencyProperty.RegisterAttached("PropertyName", typeof(string), typeof(GridViewSort), new PropertyMetadata(null));
    public static string? GetPropertyName(DependencyObject o) => (string?)o.GetValue(PropertyNameProperty);
    public static void SetPropertyName(DependencyObject o, string? v) => o.SetValue(PropertyNameProperty, v);

    private sealed class State { public string? Prop; public ListSortDirection Dir; }
    private static readonly Dictionary<ListView, State> States = new();

    public static void Attach(ListView lv, string defaultProp, ListSortDirection defaultDir)
    {
        States[lv] = new State { Prop = defaultProp, Dir = defaultDir };
        lv.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler((s, e) =>
        {
            if (e.OriginalSource is not GridViewColumnHeader h || h.Column is null) return;
            string? prop = GetPropertyName(h.Column);
            if (string.IsNullOrEmpty(prop)) return;
            var st = States[lv];
            if (st.Prop == prop) st.Dir = st.Dir == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            else { st.Prop = prop; st.Dir = prop is "Name" or "Path" or "Folder" or "TypeLabel" or "CategoryName" or "Label" ? ListSortDirection.Ascending : ListSortDirection.Descending; }
            Apply(lv);
        }));
    }

    public static void Apply(ListView lv)
    {
        if (!States.TryGetValue(lv, out var st) || st.Prop is null) return;
        var view = CollectionViewSource.GetDefaultView(lv.ItemsSource);
        if (view is null) return;
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            // le cartelle prima dei file quando si ordina per nome
            if (st.Prop == "Name" && lv.ItemsSource is IEnumerable<Row>)
                view.SortDescriptions.Add(new SortDescription("IsDir", ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(st.Prop, st.Dir));
        }
    }
}

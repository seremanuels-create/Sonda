using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Sonda.Core;
using Sonda.UI;

namespace Sonda;

public partial class MainWindow : Window
{
    private sealed class DriveChoice
    {
        public required string Path { get; init; }
        public required string Display { get; init; }
        public bool IsFolder { get; init; }
        public override string ToString() => Display;
    }

    private sealed class CatChoice
    {
        public byte? Id { get; init; }
        public required string Name { get; init; }
        public override string ToString() => Name;
    }

    private readonly string? _initialPath;
    private ScanEngine? _engine;
    private CancellationTokenSource? _cts;
    private ScanResult? _result;
    private Analysis? _analysis;
    private DirNode? _currentDir;
    private readonly Stack<DirNode> _back = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _scanning;
    private List<Row> _allTopFileRows = new();

    /// <summary>Diagnostica: se impostato, dopo l'analisi salva un PNG della finestra ed esce.</summary>
    public string? ShotPath { get; set; }
    public int ShotTab { get; set; } = -1;
    /// <summary>Diagnostica: cambia lingua a caldo prima dello scatto, per provare la ricostruzione.</summary>
    public Lang? ShotRelang { get; set; }

    public MainWindow(string? initialPath)
    {
        _initialPath = initialPath;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        _timer.Tick += Timer_Tick;
        GridViewSort.Attach(DirList, "OnDisk", ListSortDirection.Descending);
        GridViewSort.Attach(FileList, "OnDisk", ListSortDirection.Descending);
        GridViewSort.Attach(CauseFileList, "OnDisk", ListSortDirection.Descending);
        GridViewSort.Attach(GroupList, "OnDisk", ListSortDirection.Descending);
        GridViewSort.Attach(TypeList, "OnDisk", ListSortDirection.Descending);
        Treemap.ItemClicked += Treemap_ItemClicked;
        Treemap.ItemActivated += Treemap_ItemActivated;
        VolumeBarHost.SizeChanged += (_, _) => UpdateVolumeBar();
    }

    // ------------------------------------------------------------ avvio
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        bool admin = Native.IsAdministrator();
        AdminBanner.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
        ElevateButton.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
        Title = Loc.S(admin ? "app.title.admin" : "app.title");

        PopulateDrives();
        BuildLegend();

        if (!string.IsNullOrWhiteSpace(_initialPath) && (Directory.Exists(_initialPath)))
        {
            SelectPath(_initialPath);
            StartScan();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _cts?.Cancel();
    }

    private void PopulateDrives()
    {
        var items = new List<DriveChoice>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network)) continue;
                string label = string.IsNullOrEmpty(d.VolumeLabel) ? (d.DriveType == DriveType.Fixed ? Loc.S("ui.localDisk") : d.DriveType.ToString()) : d.VolumeLabel;
                items.Add(new DriveChoice
                {
                    Path = d.RootDirectory.FullName,
                    Display = $"{d.Name.TrimEnd('\\')}  {label}  —  " + Loc.S("ui.driveUsedOf", Format.Bytes(d.TotalSize - d.TotalFreeSpace), Format.Bytes(d.TotalSize)),
                });
            }
            catch { }
        }
        DriveBox.ItemsSource = items;
        string sys = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        DriveBox.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Path, sys, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
    }

    private void SelectPath(string path)
    {
        var items = (DriveBox.ItemsSource as List<DriveChoice>) ?? new List<DriveChoice>();
        string full = Path.GetFullPath(path);
        var existing = items.FirstOrDefault(i => string.Equals(i.Path.TrimEnd('\\'), full.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new DriveChoice { Path = full, Display = Loc.S("ui.folderPrefix") + full, IsFolder = true };
            items = items.Where(i => !i.IsFolder).Append(existing).ToList();
            DriveBox.ItemsSource = items;
        }
        DriveBox.SelectedItem = existing;
    }

    private void BuildLegend()
    {
        LegendPanel.Children.Clear();
        foreach (Family f in Enum.GetValues<Family>())
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2) };
            sp.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = Palette.Brush(f), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = Palette.FamilyName(f), FontSize = 12, Foreground = (Brush)FindResource("Ink2Brush") });
            LegendPanel.Children.Add(sp);
        }
    }

    // ------------------------------------------------------------ scansione
    private void DriveBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveBox.SelectedItem is DriveChoice c && !_scanning)
            ProgressText.Text = c.IsFolder ? Loc.S("ui.readyForFolder", c.Path) : Loc.S("ui.readyFor", c.Path.TrimEnd('\\'));
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.S("ui.pickFolder"), Multiselect = false };
        if (dlg.ShowDialog(this) == true && !string.IsNullOrEmpty(dlg.FolderName))
        {
            SelectPath(dlg.FolderName);
            StartScan();
        }
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_scanning) { _cts?.Cancel(); ScanButton.IsEnabled = false; ScanButton.Content = Loc.S("ui.stopping"); return; }
        StartScan();
    }

    private async void StartScan()
    {
        if (_scanning || _busy) return;
        if (DriveBox.SelectedItem is not DriveChoice choice) return;
        string path = choice.Path;

        _scanning = true;
        ScanButton.Content = Loc.S("ui.stop");
        ScanButton.IsEnabled = true;
        DriveBox.IsEnabled = false;
        ExportButton.IsEnabled = false;
        ProgressPath.Text = "";
        StatusText.Text = Loc.S("ui.scanning");
        Mouse.OverrideCursor = Cursors.AppStarting;

        _cts = new CancellationTokenSource();
        var engine = new ScanEngine();
        _engine = engine;
        _timer.Start();
        var token = _cts.Token;

        ScanResult? result = null;
        Analysis? analysis = null;
        string? error = null;
        try
        {
            result = await Task.Run(() => engine.Run(path, token), CancellationToken.None);
            ProgressText.Text = Loc.S("ui.processing");
            analysis = await Task.Run(() => Analysis.Build(result), CancellationToken.None);
        }
        catch (Exception ex) { error = ex.Message; }

        _timer.Stop();
        Mouse.OverrideCursor = null;
        _scanning = false;
        ScanButton.Content = Loc.S("ui.scan");
        ScanButton.IsEnabled = true;
        DriveBox.IsEnabled = true;

        if (error is not null || result is null || analysis is null)
        {
            ProgressText.Text = Loc.S("ui.error", error ?? "?");
            StatusText.Text = Loc.S("ui.status.error");
            return;
        }

        _result = result;
        _analysis = analysis;
        ExportButton.IsEnabled = true;
        ShowScanSummary();
        PopulateAll();

        if (ShotPath is not null)
        {
            if (ShotTab >= 0 && ShotTab < Tabs.Items.Count) Tabs.SelectedIndex = ShotTab;
            await Task.Delay(1200);
            if (ShotRelang is Lang other)
            {
                // Prova del cambio lingua a caldo: come premere Impostazioni e scegliere un'altra lingua
                Loc.Apply(other);
                var w = RebuildForLanguage();
                w.ShotPath = ShotPath;
                w.ShotTab = ShotTab;
                await Task.Delay(1500);
                w.SaveShot(ShotPath);
                w.Close();
                return;
            }
            SaveShot(ShotPath);
            Close();
        }
    }

    private void SaveShot(string path)
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(this);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }
        catch (Exception ex) { File.WriteAllText(path + ".err.txt", ex.ToString()); }
    }

    /// <summary>Riscrive i testi di stato dai dati dell'ultima scansione (anche dopo un cambio di lingua).</summary>
    private void ShowScanSummary()
    {
        if (_result is null) return;
        var st = _result.Stats;
        ProgressText.Text = Loc.S(st.Cancelled ? "ui.cancelled" : "ui.done")
            + Loc.S("ui.doneStats", Format.Count(st.Files), Format.Count(st.Dirs), Format.Duration(st.Elapsed))
            + (st.DeniedDirs > 0 ? Loc.S("ui.doneDenied", Format.Count(st.DeniedDirs)) : "");
        ProgressPath.Text = _result.ScanPath;
        StatusText.Text = Loc.S(st.Cancelled ? "ui.status.cancelled" : "ui.status.done");
        StatusRight.Text = Loc.S("ui.status.right", Format.Count(st.Files), Format.Bytes(_result.Root.OnDisk))
            + (_result.Elevated ? Loc.S("ui.status.admin") : "");
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_engine is null) return;
        var el = DateTime.Now - _engine.Live.StartedAt;
        ProgressText.Text = Loc.S("ui.progress", Format.Count(_engine.FilesSoFar), Format.Count(_engine.DirsSoFar), Format.Bytes(_engine.OnDiskSoFar), Format.Duration(el))
            + (_engine.DeniedSoFar > 0 ? Loc.S("ui.progress.denied", Format.Count(_engine.DeniedSoFar)) : "");
        ProgressPath.Text = _engine.CurrentPath;
    }

    // ------------------------------------------------------------ popolamento
    private void PopulateAll()
    {
        if (_result is null || _analysis is null) return;
        var r = _result; var a = _analysis;

        // Volume
        var v = r.Volume;
        VolumeText.Text = r.ScannedWholeVolume
            ? Loc.S("ui.volume.line", v.Display, Format.Bytes(v.UsedBytes), Format.Bytes(v.TotalBytes),
                    Format.Percent((double)v.UsedBytes / Math.Max(1, v.TotalBytes)), Format.Bytes(v.FreeBytes))
            : Loc.S("ui.volume.folder", r.ScanPath, Format.Bytes(r.Root.OnDisk), v.Display, Format.Bytes(v.FreeBytes), Format.Bytes(v.TotalBytes));
        VolumeRight.Text = r.ScannedWholeVolume
            ? Loc.S("ui.volume.right", Format.Bytes(r.Root.OnDisk), Format.Count(v.ClusterSize), v.Format)
            : Loc.S("ui.volume.rightFolder", Format.Count(r.Stats.Files), Format.Count(r.Stats.Dirs));
        UpdateVolumeBar();

        // Cause
        NoDataText.Visibility = Visibility.Collapsed;
        MainCauseContent.Visibility = Visibility.Visible;
        if (a.Main is { } main)
        {
            var cat = main.Category;
            MainCauseName.Text = cat.Name;
            MainCauseSwatch.Background = Palette.Brush(cat.Family);
            MainCauseSize.Text = Format.Bytes(main.OnDisk);
            MainCauseShare.Text = Format.Percent(main.Share) + Loc.S(r.ScannedWholeVolume ? "ui.shareOfUsed" : "ui.shareOfFolder");
            MainCauseBar.Background = Palette.Brush(cat.Family);
            MainCauseBar.Width = Math.Max(2, Math.Min(1, main.Share) * (MainCausePanel.ActualWidth > 0 ? MainCausePanel.ActualWidth : 320));
            MainCauseFiles.Text = Loc.S("ui.nFiles", Format.Count(main.Files));
            MainCauseSafety.Content = cat.Safety;
            MainCauseDesc.Text = cat.Description;
            MainCauseAction.Text = cat.Action;
            MainCauseGroups.ItemsSource = main.TopGroups.Take(6).Select(g => new GroupRow { Item = g, Share = main.OnDisk > 0 ? (double)g.OnDisk / main.OnDisk : 0 }).ToList();
        }
        long top = a.Main?.OnDisk ?? 1;
        OtherCauses.ItemsSource = a.Causes.Skip(1).Select(c => new CauseRow { Cause = c, BarShare = top > 0 ? (double)c.OnDisk / top : 0 }).ToList();
        CausePicker.ItemsSource = a.Causes.Select(c => new CauseRow { Cause = c, BarShare = 0 }).ToList();

        // Cartelle
        _back.Clear();
        NavigateTo(r.Root, push: false);

        // File più grandi
        _allTopFileRows = a.TopFiles.Select(f => Row.FromFile(f, a.ReferenceBytes)).ToList();
        var cats = new List<CatChoice> { new() { Id = null, Name = Loc.S("ui.allCategories") } };
        cats.AddRange(a.Causes.Select(c => new CatChoice { Id = c.Category.Id, Name = $"{c.Category.Name}  ({Format.Bytes(c.OnDisk)})" }));
        FileCatFilter.ItemsSource = cats;
        FileCatFilter.SelectedIndex = 0;
        FileFilter.Text = "";
        ApplyFileFilter();

        // Tipi
        TypeList.ItemsSource = a.Types.Select(t => new TypeRow { Stat = t }).ToList();
        GridViewSort.Apply(TypeList);

        // Bilancio
        BalanceList.ItemsSource = a.Balance.Select(b => new BalanceRow { Line = b }).ToList();
        DeniedHeader.Text = r.DeniedDirs.Count == 0 ? Loc.S("ui.deniedNone") : Loc.S("ui.deniedList", Format.Count(r.DeniedDirs.Count));
        DeniedList.ItemsSource = r.DeniedDirs.Select(d => Row.FromDir(d, 0)).ToList();

        // Dettaglio: parti dalla causa principale
        if (a.Main is not null) ShowCause(a.Main, switchTab: false);
    }

    private void UpdateVolumeBar()
    {
        if (_result is null) { VolumeUsedBar.Width = 0; VolumeSeenBar.Width = 0; return; }
        var v = _result.Volume;
        double w = VolumeBarHost.ActualWidth;
        if (w <= 0 || v.TotalBytes <= 0) return;
        VolumeUsedBar.Width = Math.Clamp((double)v.UsedBytes / v.TotalBytes, 0, 1) * w;
        VolumeSeenBar.Width = _result.ScannedWholeVolume ? Math.Clamp((double)_result.Root.OnDisk / v.TotalBytes, 0, 1) * w : 0;
    }

    // ------------------------------------------------------------ esploratore cartelle
    private void NavigateTo(DirNode dir, bool push = true)
    {
        if (_currentDir is not null && push && !ReferenceEquals(_currentDir, dir)) _back.Push(_currentDir);
        _currentDir = dir;

        long total = dir.OnDisk;
        var rows = new List<Row>(dir.Dirs.Count + (dir.Files?.Count ?? 0));
        foreach (var d in dir.Dirs) rows.Add(Row.FromDir(d, total));
        if (dir.Files is not null) foreach (var f in dir.Files) rows.Add(Row.FromFile(f, total));
        DirList.ItemsSource = rows;
        GridViewSort.Apply(DirList);

        // briciole
        Breadcrumb.Children.Clear();
        var chain = new List<DirNode>();
        for (DirNode? n = dir; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();
        for (int i = 0; i < chain.Count; i++)
        {
            var n = chain[i];
            var b = new Button { Content = n.IsRoot ? n.Name : n.Name, Style = (Style)FindResource("LinkButton"), Tag = n, Padding = new Thickness(2, 0, 2, 0) };
            if (i == chain.Count - 1) { b.FontWeight = FontWeights.SemiBold; b.Foreground = (Brush)FindResource("InkBrush"); }
            b.Click += (_, _) => NavigateTo((DirNode)b.Tag);
            Breadcrumb.Children.Add(b);
            if (i < chain.Count - 1) Breadcrumb.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(4, 0, 4, 0), Foreground = (Brush)FindResource("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        }
        var cat = Classifier.Get(dir.Cat);
        DirSummary.Text = Loc.S("ui.dirSummary", Format.Bytes(dir.OnDisk), Format.Count(dir.FileCount), Format.Count(dir.DirCount), cat.Name)
                        + (dir.DeniedCount > 0 ? Loc.S("ui.dirSummary.denied", Format.Count(dir.DeniedCount)) : "");
        BackButton.IsEnabled = _back.Count > 0;
        UpButton.IsEnabled = dir.Parent is not null;

        // mappa
        var items = new List<TreemapItem>();
        foreach (var d in dir.Dirs)
        {
            var kids = new List<TreemapItem>();
            foreach (var dd in d.Dirs.OrderByDescending(x => x.OnDisk).Take(40))
                kids.Add(new TreemapItem { Label = dd.Name, Weight = dd.OnDisk, Color = Palette.Color(Classifier.Get(dd.Cat).Family), IsDir = true, Tag = dd });
            if (d.Files is not null)
                foreach (var ff in d.Files.OrderByDescending(x => x.OnDisk).Take(40))
                    kids.Add(new TreemapItem { Label = ff.Name, Weight = ff.OnDisk, Color = Palette.Color(Classifier.Get(ff.Cat).Family), IsDir = false, Tag = ff });
            items.Add(new TreemapItem
            {
                Label = d.Name, Weight = d.OnDisk, Color = Palette.Color(Classifier.Get(d.Cat).Family), IsDir = true, Tag = d, Children = kids,
                Tooltip = Loc.S("tip.dir", d.FullPath, Format.Bytes(d.OnDisk), Format.Count(d.FileCount), Classifier.Get(d.Cat).Name)
            });
        }
        if (dir.Files is not null)
            foreach (var f in dir.Files)
                items.Add(new TreemapItem
                {
                    Label = f.Name, Weight = f.OnDisk, Color = Palette.Color(Classifier.Get(f.Cat).Family), IsDir = false, Tag = f,
                    Tooltip = Loc.S("tip.file", f.FullPath, Format.Bytes(f.OnDisk), Format.Bytes(f.Size), Classifier.Describe(f), Classifier.Get(f.Cat).Name)
                });
        Treemap.SetItems(items);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_back.Count > 0) NavigateTo(_back.Pop(), push: false);
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDir?.Parent is { } p) NavigateTo(p);
    }

    private void OpenCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDir is not null) ShellOps.OpenFolder(_currentDir.FullPath);
    }

    private void DirList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DirList.SelectedItem is Row r) Treemap.SetSelected((object?)r.Node ?? r.File);
    }

    private void Treemap_ItemClicked(TreemapItem item)
    {
        if (DirList.ItemsSource is not List<Row> rows) return;
        var row = rows.FirstOrDefault(x => ReferenceEquals(x.Node, item.Tag) || ReferenceEquals(x.File, item.Tag));
        if (row is not null) { DirList.SelectedItem = row; DirList.ScrollIntoView(row); }
    }

    private void Treemap_ItemActivated(TreemapItem item)
    {
        if (item.Tag is DirNode d && !d.IsReparse && !d.IsAccessDenied) NavigateTo(d);
        else if (item.Tag is FileEntry f) ShellOps.RevealInExplorer(f.FullPath);
    }

    private void Row_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem lvi) return;
        if (lvi.DataContext is Row r)
        {
            if (r.IsDir && r.Node is { } d && !d.IsReparse)
            {
                NavigateTo(d);
                Tabs.SelectedIndex = 0;
            }
            else ShellOps.RevealInExplorer(r.Path);
        }
    }

    // ------------------------------------------------------------ menu contestuale
    private static object? MenuTarget(object sender)
    {
        if (sender is not MenuItem mi) return null;
        var cm = mi.Parent as ContextMenu ?? (mi.Parent as MenuItem)?.Parent as ContextMenu;
        return (cm?.PlacementTarget as FrameworkElement)?.DataContext;
    }

    private static string? PathOf(object? ctx) => ctx switch
    {
        Row r => r.Path,
        GroupRow g => g.Path,
        _ => null,
    };

    private void RowOpen_Click(object sender, RoutedEventArgs e)
    {
        var ctx = MenuTarget(sender);
        string? p = PathOf(ctx);
        if (p is null) return;
        if (Directory.Exists(p)) ShellOps.OpenFolder(p); else ShellOps.RevealInExplorer(p);
    }

    private void RowReveal_Click(object sender, RoutedEventArgs e)
    {
        string? p = PathOf(MenuTarget(sender));
        if (p is not null) ShellOps.RevealInExplorer(p);
    }

    private void RowEnter_Click(object sender, RoutedEventArgs e)
    {
        var ctx = MenuTarget(sender);
        DirNode? d = ctx switch { Row r => r.Node ?? r.File?.Dir, GroupRow g => g.Item.Dir, _ => null };
        if (d is not null && !d.IsReparse) { NavigateTo(d); Tabs.SelectedIndex = 0; }
    }

    private void RowCopy_Click(object sender, RoutedEventArgs e)
    {
        string? p = PathOf(MenuTarget(sender));
        if (p is not null) { try { Clipboard.SetText(p); StatusText.Text = Loc.S("exp.copied", p); } catch { } }
    }

    private void RowProps_Click(object sender, RoutedEventArgs e)
    {
        string? p = PathOf(MenuTarget(sender));
        if (p is not null) ShellOps.OpenProperties(p);
    }

    private void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        var ctx = MenuTarget(sender);
        // se il clic è su una lista con selezione multipla, elimina tutta la selezione
        ListView? lv = (sender as MenuItem)?.Parent is ContextMenu cm && cm.PlacementTarget is ListViewItem lvi ? ItemsControl.ItemsControlFromItemContainer(lvi) as ListView : null;
        var rows = new List<Row>();
        if (lv is not null && lv.SelectedItems.Count > 1 && lv.SelectedItems.OfType<Row>().Any(x => ReferenceEquals(x, ctx)))
            rows.AddRange(lv.SelectedItems.OfType<Row>());
        else if (ctx is Row r) rows.Add(r);
        else if (ctx is GroupRow g)
        {
            // La riga di un gruppo è un RAGGRUPPAMENTO: si elimina in blocco solo se è davvero una cartella
            // della causa mostrata e non la radice. Altrimenti si manda l'utente nell'esploratore.
            var d = g.Item.Dir;
            byte causeCat = (CausePicker.SelectedItem as CauseRow)?.Cause.Category.Id ?? d.Cat;
            if (d.IsRoot || d.Cat != causeCat)
            {
                MessageBox.Show(this, Loc.S("del.group", d.FullPath), Loc.S("del.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                if (!d.IsReparse) { NavigateTo(d); Tabs.SelectedIndex = 0; }
                return;
            }
            rows.Add(Row.FromDir(d, 0));
        }
        DeleteRows(rows);
    }

    private void DeleteSelectedFiles_Click(object sender, RoutedEventArgs e)
    {
        var rows = FileList.SelectedItems.OfType<Row>().ToList();
        if (rows.Count == 0) { MessageBox.Show(this, Loc.S("del.selectFirst"), Loc.S("app.name"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
        DeleteRows(rows);
    }

    private bool _busy;

    private void DeleteRows(List<Row> rows)
    {
        if (rows.Count == 0) return;
        if (_busy || _scanning) { StatusText.Text = Loc.S("del.busy"); return; }
        if (rows.Any(r => r.Node is { IsRoot: true }))
        {
            MessageBox.Show(this, Loc.S("del.noRoot"), Loc.S("del.title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // blocca le cose che non vanno toccate
        var keep = rows.Where(r => r.Safety == Safety.Keep).ToList();
        long total = rows.Sum(r => r.OnDisk);
        string what = rows.Count == 1 ? $"“{rows[0].Name}”" : Loc.S("del.nItems", rows.Count);
        var sb = new System.Text.StringBuilder();
        sb.Append(Loc.S("del.question", what, Format.Bytes(total)));
        foreach (var r in rows.Take(12)) sb.Append("  •  ").Append(r.Path).Append('\n');
        if (rows.Count > 12) sb.Append(Loc.S("del.more", rows.Count - 12));
        sb.Append('\n');
        if (keep.Count > 0)
            sb.Append(Loc.S("del.keepWarn", keep.Count, string.Join(", ", keep.Select(k => k.CategoryName).Distinct().Take(3))));
        sb.Append(Loc.S("del.footer"));
        var res = MessageBox.Show(this, sb.ToString(), Loc.S("del.title"), MessageBoxButton.YesNo, keep.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No);
        if (res != MessageBoxResult.Yes) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        string? err = ShellOps.Recycle(hwnd, rows.Select(r => r.Path).ToList(), confirm: false);
        if (err is not null)
        {
            MessageBox.Show(this, err, Loc.S("del.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        // Aggiorna il modello con quello che è sparito davvero
        int removed = 0; long freed = 0;
        foreach (var r in rows)
        {
            bool gone = r.IsDir ? !Directory.Exists(r.Path) : !File.Exists(r.Path);
            if (!gone) continue;
            if (r.IsDir && r.Node is { } d) { if (RemoveDir(d)) { removed++; freed += d.OnDisk; } }
            else if (r.File is { } f) { if (RemoveFile(f)) { removed++; freed += f.OnDisk; } }
        }
        if (removed > 0)
        {
            StatusText.Text = Loc.S("del.done", removed, Format.Bytes(freed));
            Reanalyze();
        }
    }

    /// <summary>Toglie il file dal modello e aggiorna i totali. false se era già stato tolto.</summary>
    private static bool RemoveFile(FileEntry f)
    {
        var d = f.Dir;
        if (d.Files is null || !d.Files.Remove(f)) return false;
        d.OwnSize -= f.Size; d.OwnOnDisk -= f.OnDisk;
        for (DirNode? n = d; n is not null; n = n.Parent) { n.Size -= f.Size; n.OnDisk -= f.OnDisk; n.FileCount--; }
        return true;
    }

    private static bool RemoveDir(DirNode d)
    {
        var p = d.Parent;
        if (p is null || !p.Dirs.Remove(d)) return false;
        for (DirNode? n = p; n is not null; n = n.Parent) { n.Size -= d.Size; n.OnDisk -= d.OnDisk; n.FileCount -= d.FileCount; n.DirCount -= 1 + d.DirCount; n.DeniedCount -= d.DeniedCount; }
        return true;
    }

    private async void Reanalyze()
    {
        if (_result is null || _busy) return;
        var r = _result;
        _busy = true;
        // aggiorna spazio libero del volume
        if (Native.TryGetVolumeSpace(r.Volume.RootPath, out long total, out long free)) { r.Volume.TotalBytes = total; r.Volume.FreeBytes = free; }
        Mouse.OverrideCursor = Cursors.AppStarting;
        try
        {
            var a = await Task.Run(() => Analysis.Build(r));
            _analysis = a;
            var cur = _currentDir;
            var back = _back.ToArray();
            PopulateAll();
            // Torna dov'eri, tenendo nello stack solo le cartelle che esistono ancora.
            // Ciclo all'indietro invece di back.Reverse(): su un array quella chiamata può risolversi a
            // MemoryExtensions.Reverse (che ordina sul posto e restituisce void) invece che a Enumerable.Reverse,
            // e allora non compila nemmeno. ToArray() di uno Stack dà dalla cima al fondo: si ripusha dal fondo.
            _back.Clear();
            for (int i = back.Length - 1; i >= 0; i--) if (StillInTree(back[i])) _back.Push(back[i]);
            if (cur is not null && StillInTree(cur)) NavigateTo(cur, push: false);
        }
        catch (Exception ex) { StatusText.Text = Loc.S("del.recalcError", ex.Message); }
        finally { Mouse.OverrideCursor = null; _busy = false; }
    }

    private bool StillInTree(DirNode d)
    {
        for (DirNode? n = d; n is not null; n = n.Parent)
        {
            if (n.Parent is null) return ReferenceEquals(n, _result?.Root);
            if (!n.Parent.Dirs.Contains(n)) return false;
        }
        return false;
    }

    // ------------------------------------------------------------ cause
    private void MainCause_Click(object sender, RoutedEventArgs e)
    {
        if (_analysis?.Main is { } m) ShowCause(m, switchTab: true);
    }

    private void Cause_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is CauseRow cr) ShowCause(cr.Cause, switchTab: true);
    }

    private void GroupLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is GroupRow g)
        {
            var d = g.Item.Dir;
            if (!d.IsReparse) { NavigateTo(d); Tabs.SelectedIndex = 0; }
        }
    }

    private bool _settingPicker;
    private void CausePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingPicker) return;
        if (CausePicker.SelectedItem is CauseRow cr) ShowCause(cr.Cause, switchTab: false);
    }

    private void ShowCause(Cause c, bool switchTab)
    {
        _settingPicker = true;
        CausePicker.SelectedItem = (CausePicker.ItemsSource as List<CauseRow>)?.FirstOrDefault(x => ReferenceEquals(x.Cause, c));
        _settingPicker = false;
        var cat = c.Category;
        CauseSize.Text = Format.Bytes(c.OnDisk);
        CauseShare.Text = Loc.S("ui.causeStats", Format.Percent(c.Share), Format.Count(c.Files), c.Rank, _analysis?.Causes.Count ?? 0);
        CauseSafety.Content = cat.Safety;
        CauseDesc.Text = cat.Description;
        CauseAction.Text = Loc.S("ui.causeAction", cat.Action);
        GroupList.ItemsSource = c.TopGroups.Select(g => new GroupRow { Item = g, Share = c.OnDisk > 0 ? (double)g.OnDisk / c.OnDisk : 0 }).ToList();
        GridViewSort.Apply(GroupList);
        CauseFileList.ItemsSource = c.TopFiles.Select(f => Row.FromFile(f, c.OnDisk)).ToList();
        GridViewSort.Apply(CauseFileList);
        if (switchTab) Tabs.SelectedItem = CauseTab;
    }

    private void GroupList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GroupList.SelectedItem is GroupRow g && !g.Item.Dir.IsReparse) { NavigateTo(g.Item.Dir); Tabs.SelectedIndex = 0; }
    }

    // ------------------------------------------------------------ file più grandi
    private void FileFilter_TextChanged(object sender, RoutedEventArgs e) => ApplyFileFilter();

    private void ApplyFileFilter()
    {
        if (_analysis is null) return;
        string q = FileFilter.Text.Trim();
        byte? cat = (FileCatFilter.SelectedItem as CatChoice)?.Id;
        IEnumerable<Row> rows = _allTopFileRows;
        if (cat is byte c) rows = rows.Where(r => r.File is not null && r.File.Cat == c);
        if (q.Length > 0) rows = rows.Where(r => r.Path.Contains(q, StringComparison.OrdinalIgnoreCase) || r.What.Contains(q, StringComparison.OrdinalIgnoreCase));
        var list = rows.ToList();
        FileList.ItemsSource = list;
        GridViewSort.Apply(FileList);
        FilesSummary.Text = Loc.S("ui.filesSummary", Format.Count(list.Count), Format.Count(_allTopFileRows.Count), Format.Bytes(list.Sum(r => r.OnDisk)));
    }

    private void TypeList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TypeList.SelectedItem is not TypeRow tr || _result is null) return;
        ushort tid = tr.Stat.Type.Id;
        // raccogli i file più grandi di questo tipo in tutto l'albero
        var pq = new PriorityQueue<FileEntry, long>();
        var stack = new Stack<DirNode>(); stack.Push(_result.Root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            foreach (var c in d.Dirs) stack.Push(c);
            if (d.Files is null) continue;
            foreach (var f in d.Files)
            {
                if (f.TypeId != tid) continue;
                if (pq.Count < 1000) pq.Enqueue(f, f.OnDisk);
                else if (pq.TryPeek(out _, out long min) && f.OnDisk > min) { pq.Dequeue(); pq.Enqueue(f, f.OnDisk); }
            }
        }
        var files = new List<FileEntry>();
        while (pq.TryDequeue(out var f, out _)) files.Add(f);
        files.Reverse();
        _allTopFileRows = files.Select(f => Row.FromFile(f, _analysis!.ReferenceBytes)).ToList();
        FileCatFilter.SelectedIndex = 0;
        FileFilter.Text = "";
        ApplyFileFilter();
        FilesSummary.Text = Loc.S("ui.typeSummary", tr.Label, Format.Count(files.Count), Format.Bytes(files.Sum(x => x.OnDisk)));
        Tabs.SelectedIndex = 1;
    }

    // ------------------------------------------------------------ esportazioni
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.ContextMenu is not null) { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; }
    }

    private string? AskSave(string filter, string name)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = name, Title = Loc.S("exp.title") };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    private void ExportText_Click(object sender, RoutedEventArgs e)
    {
        if (_analysis is null) return;
        var p = AskSave(Loc.S("exp.text"), $"Sonda-{DateTime.Now:yyyyMMdd-HHmm}.txt");
        if (p is null) return;
        try { File.WriteAllText(p, Report.BuildText(_analysis, 300, 30), System.Text.Encoding.UTF8); StatusText.Text = Loc.S("exp.saved", p); ShellOps.RevealInExplorer(p); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.S("exp.title"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportFilesCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_analysis is null) return;
        var p = AskSave(Loc.S("exp.csv"), $"Sonda-file-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (p is null) return;
        try { Report.WriteFilesCsv(p, _analysis.TopFiles); StatusText.Text = Loc.S("exp.savedCsv", p); ShellOps.RevealInExplorer(p); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.S("exp.title"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportDirsCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null) return;
        var p = AskSave(Loc.S("exp.csv"), $"Sonda-cartelle-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (p is null) return;
        try
        {
            var dirs = new List<DirNode>();
            var stack = new Stack<DirNode>(); stack.Push(_result.Root);
            while (stack.Count > 0) { var d = stack.Pop(); dirs.Add(d); foreach (var c in d.Dirs) stack.Push(c); }
            dirs.Sort((a, b) => b.OnDisk.CompareTo(a.OnDisk));
            Report.WriteDirsCsv(p, dirs.Take(20000));
            StatusText.Text = Loc.S("exp.savedCsv", p); ShellOps.RevealInExplorer(p);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.S("exp.title"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportCausesCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_analysis is null) return;
        var p = AskSave(Loc.S("exp.csv"), $"Sonda-cause-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (p is null) return;
        try { Report.WriteCausesCsv(p, _analysis); StatusText.Text = Loc.S("exp.savedCsv", p); ShellOps.RevealInExplorer(p); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.S("exp.title"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    // ------------------------------------------------------------ impostazioni e lingua
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow { Owner = this };
        dlg.ShowDialog();
        if (dlg.LanguageChanged) RebuildForLanguage();
    }

    /// <summary>
    /// Ricostruisce la finestra nella nuova lingua. Le stringhe del XAML si risolvono quando la finestra
    /// viene creata, quindi si crea una finestra nuova; il risultato della scansione viene passato così com'è
    /// (l'albero non contiene testo tradotto: nomi e descrizioni si leggono dalle chiavi al momento).
    /// </summary>
    private MainWindow RebuildForLanguage()
    {
        var w = new MainWindow(null)
        {
            Left = Left, Top = Top, Width = Width, Height = Height, WindowState = WindowState,
            _result = _result, _analysis = _analysis,
        };
        var app = Application.Current;
        if (app is not null) app.MainWindow = w;
        w.Show();
        // La vecchia finestra si chiude dopo: senza questo, chiudendo l'ultima finestra l'app terminerebbe.
        Closing -= OnClosing;
        Close();
        if (w._result is not null && w._analysis is not null)
        {
            w.ExportButton.IsEnabled = true;
            w.PopulateAll();
            w.SelectPath(w._result.ScanPath);
            w.ShowScanSummary();
        }
        return w;
    }

    // ------------------------------------------------------------ elevazione
    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        string? arg = (DriveBox.SelectedItem as DriveChoice)?.Path?.TrimEnd('\\');
        if (ShellOps.RestartElevated(arg is null ? null : $"\"{arg}\"")) Close();
    }
}

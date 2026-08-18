using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Sonda.Core;

namespace Sonda;

public partial class SettingsWindow : Window
{
    private sealed class LangChoice
    {
        public required Lang Value { get; init; }
        public required string Name { get; init; }
        public override string ToString() => Name;
    }

    private bool _loading = true;

    /// <summary>Vero se la lingua è stata cambiata: la finestra principale si ricostruisce.</summary>
    public bool LanguageChanged { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        var choices = new List<LangChoice>
        {
            new() { Value = Lang.Auto, Name = Loc.LangName(Lang.Auto) },
            new() { Value = Lang.Italiano, Name = "Italiano" },
            new() { Value = Lang.English, Name = "English" },
        };
        LangBox.ItemsSource = choices;
        LangBox.SelectedItem = choices.First(c => c.Value == Loc.Setting);

        string version = Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
        AboutText.Text = Loc.S("settings.about", version);
        _loading = false;
    }

    private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LangBox.SelectedItem is not LangChoice c) return;
        if (c.Value == Loc.Setting) return;
        Loc.SaveSettings(c.Value);
        LanguageChanged = true;
        Close();   // la finestra principale si ricostruisce nella nuova lingua
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

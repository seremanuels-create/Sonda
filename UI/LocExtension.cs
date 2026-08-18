using System.Windows.Markup;
using Sonda.Core;

namespace Sonda.UI;

/// <summary>
/// Stringa localizzata dentro il XAML: <c>Text="{ui:S ui.scan}"</c>.
///
/// Il valore si risolve una volta sola, quando la finestra viene costruita: il cambio di lingua
/// ricrea la finestra (MainWindow.RicreaPerLingua), quindi non serve un binding che notifichi.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class SExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public SExtension() { }
    public SExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.S(Key);
}

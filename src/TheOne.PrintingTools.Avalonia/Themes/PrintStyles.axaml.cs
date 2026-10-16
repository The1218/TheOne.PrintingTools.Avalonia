using global::Avalonia.Markup.Xaml;
using global::Avalonia.Styling;

namespace TheOne.PrintingTools.Avalonia;

internal sealed partial class PrintStyles : Styles
{
    public PrintStyles() => AvaloniaXamlLoader.Load(this);
}

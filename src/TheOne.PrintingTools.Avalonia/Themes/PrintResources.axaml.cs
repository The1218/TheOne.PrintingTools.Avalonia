using global::Avalonia.Controls;
using global::Avalonia.Markup.Xaml;

namespace TheOne.PrintingTools.Avalonia;

internal sealed partial class PrintResources : ResourceDictionary
{
    public PrintResources() => AvaloniaXamlLoader.Load(this);
}

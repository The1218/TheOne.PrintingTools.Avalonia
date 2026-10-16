using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TheOne.PrintingTools.Avalonia;

internal sealed class PrintResources : ResourceDictionary
{
    public PrintResources()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
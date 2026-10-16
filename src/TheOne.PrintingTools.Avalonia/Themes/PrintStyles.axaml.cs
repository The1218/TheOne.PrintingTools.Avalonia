using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace TheOne.PrintingTools.Avalonia;

internal sealed class PrintStyles : Styles
{
    public PrintStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
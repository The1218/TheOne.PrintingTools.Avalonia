using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace TheOne.PrintingTools.Avalonia;

/// <summary>Standalone print preview theme. Register explicitly in Application.Styles.</summary>
public sealed class PrintingToolsTheme : Styles
{
    public PrintingToolsTheme()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
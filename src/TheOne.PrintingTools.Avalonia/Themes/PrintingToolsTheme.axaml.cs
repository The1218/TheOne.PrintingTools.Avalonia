using global::Avalonia.Markup.Xaml;
using global::Avalonia.Styling;

namespace TheOne.PrintingTools.Avalonia;

/// <summary>Standalone print preview theme. Register explicitly in Application.Styles.</summary>
public sealed partial class PrintingToolsTheme : Styles
{
    public PrintingToolsTheme() => AvaloniaXamlLoader.Load(this);
}

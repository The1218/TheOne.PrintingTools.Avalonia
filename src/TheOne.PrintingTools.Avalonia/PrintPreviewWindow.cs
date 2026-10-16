using Avalonia.Controls;
using Avalonia.Input;

namespace TheOne.PrintingTools.Avalonia;

/// <summary>Print dialog. The consumer registers PrintingToolsTheme in Application.Styles.</summary>
public sealed class PrintPreviewWindow : Window
{
    public PrintPreviewWindow(PrintDocument document, PrintService? service = null, PrintSettings? settings = null)
    {
        Title = "打印预览 · " + document.Title;
        Width = 1440;
        Height = 1000;
        MinWidth = 460;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Preview = new PrintPreview(document, service, settings);
        Content = Preview;
        Preview.CloseRequested += (_, result) => Close(result);
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Preview.Cancel();
                args.Handled = true;
            }
        };
        Opened += (_, _) => Preview.Focus();
        Closing += (_, args) =>
        {
            if (Preview.IsBusy)
            {
                args.Cancel = true;
                Preview.Cancel();
            }
        };
    }

    public PrintPreview Preview { get; }

    public void Cancel()
    {
        Preview.Cancel();
    }
}
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace TheOne.PrintingTools.Avalonia;

public sealed partial class PrintPreview
{
    // One 20 DIP icon grid; filled outlines inherit the control foreground in every theme/state.
    private const string ChevronLeftPath = "M12.5,3.5 L6,10 L12.5,16.5 L13.6,15.4 L8.2,10 L13.6,4.6 Z";
    private const string ChevronRightPath = "M7.5,3.5 L14,10 L7.5,16.5 L6.4,15.4 L11.8,10 L6.4,4.6 Z";
    private const string ChevronUpPath = "M3.5,12.5 L10,6 L16.5,12.5 L15.4,13.6 L10,8.2 L4.6,13.6 Z";
    private const string ChevronDownPath = "M3.5,7.5 L10,14 L16.5,7.5 L15.4,6.4 L10,11.8 L4.6,6.4 Z";
    private const string MinusPath = "M3,9.25 H17 V10.75 H3 Z";
    private const string PlusPath = "M9.25,3 H10.75 V9.25 H17 V10.75 H10.75 V17 H9.25 V10.75 H3 V9.25 H9.25 Z";

    private const string PrinterPath =
        "F0 M5,1 H15 V6 H5 Z M6.5,2.5 V6 H13.5 V2.5 Z M4,5.5 H16 Q19,5.5 19,8.5 V14 H15 V19 H5 V14 H1 V8.5 Q1,5.5 4,5.5 Z M4,7 Q2.5,7 2.5,8.5 V12.5 H5 V11 H15 V12.5 H17.5 V8.5 Q17.5,7 16,7 Z M6.5,12.5 V17.5 H13.5 V12.5 Z M14.5,8.5 H16 V10 H14.5 Z";

    private readonly RadioButton[] _colorRadios =
        [new() { Content = "黑白", Name = "PrintMonochrome" }, new() { Content = "彩色", Name = "PrintColorOutput" }];

    private readonly RadioButton[] _orientationRadios =
        [new() { Content = "纵向", Name = "PrintPortrait" }, new() { Content = "横向", Name = "PrintLandscape" }];

    private readonly RadioButton[] _rangeRadios =
    [
        new() { Content = "全部", Name = "PrintRangeAll" }, new() { Content = "当前页", Name = "PrintRangeCurrent" },
        new() { Content = "自定义", Name = "PrintRangeCustom" }
    ];

    private readonly Dictionary<int, Bitmap> _thumbnailBitmaps = [];
    private readonly HashSet<int> _thumbnailPending = [];

    private readonly ListBox _thumbnails = new()
        { Name = "PrintThumbnails", Background = Brushes.Transparent, BorderThickness = new Thickness(0) };

    private StackPanel _branding = null!, _footerActions = null!;
    private Grid _copySpinner = null!;
    private Button _refresh = null!;
    private (float Width, float Height) _renderedSize;
    private TextBlock _subtitle = null!;
    private Border _thumbnailFrame = null!;
    private int _thumbnailGeneration;
    private PrintMargins? _thumbnailMargins;
    private PrintSettings? _thumbnailSettings;
    private TextBlock _totalLabel = null!;
    private string? _workspaceColumns;
    private Button _zoomOut = null!, _zoomIn = null!;

    private Button BuildLayout()
    {
        _settings.Spacing = 0;
        _settings.Margin = new Thickness(24, 20);
        var panelTitle = Text("打印设置");
        panelTitle.FontSize = 16;
        panelTitle.FontWeight = FontWeight.SemiBold;
        panelTitle.Margin = new Thickness(0, 0, 0, 22);
        _settings.Children.Add(panelTitle);
        var printerLabel = Text("打印机");
        printerLabel.Margin = new Thickness(0, 0, 0, 8);
        _settings.Children.Add(printerLabel);
        _settings.Children.Add(_printers);
        var dot = new Border
            { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
        dot.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PrintingTools.Success"));
        var printerStatus = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 9, Margin = new Thickness(10, 8, 0, 0),
            Children = { dot, _printerState }
        };
        _settings.Children.Add(printerStatus);
        _refresh = new Button
        {
            Name = "PrintRefresh", Content = "刷新打印机", HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _settings.Children.Add(_refresh);
        var refreshMenu = new MenuItem { Header = "刷新打印机" };
        refreshMenu.Click += async (_, _) => await RefreshPrintersAsync();
        _printers.ContextMenu = new ContextMenu { ItemsSource = new[] { refreshMenu } };
        Divider();
        var rangeTitle = Text("打印范围");
        rangeTitle.Margin = new Thickness(0, 0, 0, 12);
        _settings.Children.Add(rangeTitle);
        ConfigureRadios(_rangeRadios, _rangeMode);
        foreach (var radio in _rangeRadios.Take(2))
        {
            radio.Margin = new Thickness(0, 0, 0, 9);
            _settings.Children.Add(radio);
        }

        var customRange = new Grid
            { ColumnDefinitions = new ColumnDefinitions("104,*"), Margin = new Thickness(0, 0, 0, 4) };
        customRange.Children.Add(_rangeRadios[2]);
        Grid.SetColumn(_range, 1);
        customRange.Children.Add(_range);
        _range.IsVisible = true;
        _settings.Children.Add(customRange);
        var example = Text("例如：1-3,5", true);
        example.FontSize = 12;
        example.Margin = new Thickness(104, 0, 0, 24);
        _settings.Children.Add(example);
        _copies.ShowButtonSpinner = false;
        _copySpinner = new Grid { ColumnDefinitions = new ColumnDefinitions("*,30"), Height = 40 };
        _copySpinner.Children.Add(_copies);
        var up = new Button
        {
            Name = "PrintCopiesUp", Content = Icon(ChevronUpPath, 12), Padding = new Thickness(0), MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(0, 4, 0, 0),
            BorderThickness = new Thickness(0, 1, 1, 0)
        };
        var down = new Button
        {
            Name = "PrintCopiesDown", Content = Icon(ChevronDownPath, 12), Padding = new Thickness(0), MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(0, 0, 4, 0),
            BorderThickness = new Thickness(0, 1, 1, 1)
        };
        _copies.CornerRadius = new CornerRadius(4, 0, 0, 4);
        up.Click += (_, _) => _copies.Value = Math.Min(999, (_copies.Value ?? 1) + 1);
        down.Click += (_, _) => _copies.Value = Math.Max(1, (_copies.Value ?? 1) - 1);
        AutomationProperties.SetName(up, "增加份数");
        AutomationProperties.SetName(down, "减少份数");
        var spinButtons = new Grid { RowDefinitions = new RowDefinitions("*,*") };
        spinButtons.Children.Add(up);
        Grid.SetRow(down, 1);
        spinButtons.Children.Add(down);
        Grid.SetColumn(spinButtons, 1);
        _copySpinner.Children.Add(spinButtons);
        AddSettingRow("份数", _copySpinner);
        Divider();
        var paperLabel = Text("纸张");
        paperLabel.Margin = new Thickness(0, 0, 0, 8);
        _settings.Children.Add(paperLabel);
        _settings.Children.Add(_paper);
        _paper.Margin = new Thickness(0, 0, 0, 22);
        ConfigureRadios(_orientationRadios, _orientation);
        ConfigureRadios(_colorRadios, _color);
        AddSettingRow("方向",
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 20,
                Children = { _orientationRadios[0], _orientationRadios[1] }
            });
        AddSettingRow("颜色",
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 20, Children = { _colorRadios[0], _colorRadios[1] }
            });
        AddSettingRow("双面打印", _duplex);
        AddSettingRow("缩放", _scale);
        _percent.Margin = new Thickness(104, 0, 0, 0);
        _settings.Children.Add(_percent);
        AddImageOptions();
        // Keep the B mockup's settings surface compact. Additional settings remain available in the API.
        foreach (var hidden in new Control[] { _rangeMode, _orientation, _color, _margin, _collate })
        {
            hidden.IsVisible = false;
            _settings.Children.Add(hidden);
        }

        _status.Name = "PrintStatus";
        _status.Margin = new Thickness(0, 16, 0, 0);
        _status.IsVisible = false;
        _status.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBlock.TextProperty) _status.IsVisible = !string.IsNullOrWhiteSpace(_status.Text);
        };
        _settings.Children.Add(_status);
        _settingsFrame =
            Frame(
                new ScrollViewer { Content = _settings, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                "PrintingTools.Surface");
        _settingsFrame.Name = "PrintSettingsFrame";
        _settingsFrame.BorderThickness = new Thickness(1, 0, 0, 0);
        Grid.SetColumn(_settingsFrame, 2);
        _body.Children.Add(_settingsFrame);

        _thumbnails.Classes.Add("print-thumbnails");
        _thumbnails.Margin = new Thickness(8, 0);
        _thumbnails.ItemTemplate = new FuncDataTemplate<int>((page, _) => CreateThumbnail(page));
        _thumbnails.SelectionChanged += (_, _) =>
        {
            if (!_updating && _thumbnails.SelectedItem is int index && index != _page) GoToPage(index + 1);
        };
        var thumbGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var thumbTitle = Text("页面缩略图");
        thumbTitle.FontWeight = FontWeight.SemiBold;
        thumbTitle.Margin = new Thickness(24, 20, 0, 18);
        thumbGrid.Children.Add(thumbTitle);
        Grid.SetRow(_thumbnails, 1);
        thumbGrid.Children.Add(_thumbnails);
        _totalLabel = Text($"共 {PageCount} 页", true);
        _totalLabel.Margin = new Thickness(38, 18, 38, 34);
        Grid.SetRow(_totalLabel, 2);
        thumbGrid.Children.Add(_totalLabel);
        _thumbnailFrame = Frame(thumbGrid, "PrintingTools.ThumbnailSurface");
        _thumbnailFrame.BorderThickness = new Thickness(0, 0, 1, 0);
        _body.Children.Add(_thumbnailFrame);

        _sheet = new Border
        {
            Child = _image, Background = Brushes.White, Margin = new Thickness(32, 34),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, Color = Color.Parse("#18000000") }),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        _previewScroll = new ScrollViewer
        {
            Content = _sheet, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = true
        };
        _previous.Content = Icon(ChevronLeftPath);
        _next.Content = Icon(ChevronRightPath);
        var minus = _zoomOut = new Button { Name = "PrintZoomOut", Content = Icon(MinusPath) };
        var plus = _zoomIn = new Button { Name = "PrintZoomIn", Content = Icon(PlusPath) };
        foreach (var button in new[] { _previous, _next, minus, plus }) button.Classes.Add("print-icon");
        foreach (var (button, label) in new[] { (_previous, "上一页"), (_next, "下一页"), (minus, "缩小预览"), (plus, "放大预览") })
            ToolTip.SetTip(button, label);
        _pageLabel.MinWidth = 76;
        _pageLabel.TextAlignment = TextAlignment.Center;
        AutomationProperties.SetName(minus, "缩小预览");
        AutomationProperties.SetName(plus, "放大预览");
        minus.Click += (_, _) => _zoom.SelectedIndex = Math.Max(1, _zoom.SelectedIndex - 1);
        plus.Click += (_, _) => _zoom.SelectedIndex = Math.Min(5, _zoom.SelectedIndex + 1);
        var separator = new Border { Width = 1, Height = 24, Margin = new Thickness(12, 0) };
        separator.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PrintingTools.Line"));
        _navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 18),
            Children = { _previous, _pageLabel, _next, separator, minus, _zoom, plus }
        };
        var previewGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        previewGrid.Children.Add(_previewScroll);
        Grid.SetRow(_navigation, 1);
        previewGrid.Children.Add(_navigation);
        _previewFrame = Frame(previewGrid, "PrintingTools.Canvas");
        _previewFrame.Name = "PrintCanvasFrame";
        Grid.SetColumn(_previewFrame, 1);
        _body.Children.Add(_previewFrame);

        _footer.ColumnDefinitions = new ColumnDefinitions("*,Auto");
        _footer.Margin = new Thickness(24, 12);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        _footer.Children.Add(_summary);
        _footerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center,
            Children = { _export, _cancel, _print }
        };
        foreach (var button in new[] { _export, _cancel, _print })
        {
            button.MinWidth = 128;
            button.MinHeight = 42;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
        }

        Grid.SetColumn(_footerActions, 1);
        _footer.Children.Add(_footerActions);

        var brandText = Text("TheOne Printing");
        brandText.FontSize = 20;
        brandText.FontWeight = FontWeight.SemiBold;
        _branding = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children = { new PrintingMark { Width = 24, Height = 24 }, brandText }, Margin = new Thickness(0, 0, 24, 0)
        };
        var titleSeparator = new Border { Width = 1, Height = 24, Margin = new Thickness(0, 0, 24, 0) };
        titleSeparator.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PrintingTools.Line"));
        var title = Text("打印预览");
        title.FontSize = 18;
        title.FontWeight = FontWeight.SemiBold;
        title.Margin = new Thickness(0, 0, 20, 0);
        _subtitle = Text($"{Document.Title} · {PageCount} 页", true);
        _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _mobileTab.MinWidth = 130;
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(24, 12),
            Children = { _branding, titleSeparator, title, _subtitle, _mobileTab }
        };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var headerFrame = Frame(header, "PrintingTools.Surface");
        headerFrame.MinHeight = 52;
        headerFrame.BorderThickness = new Thickness(0, 0, 0, 1);
        root.Children.Add(headerFrame);
        Grid.SetRow(_body, 1);
        root.Children.Add(_body);
        var footerFrame = Frame(_footer, "PrintingTools.Surface");
        footerFrame.MinHeight = 72;
        footerFrame.BorderThickness = new Thickness(0, 1, 0, 0);
        Grid.SetRow(footerFrame, 2);
        root.Children.Add(footerFrame);
        Content = root;
        return _refresh;
    }

    private void ConfigureRadios(RadioButton[] radios, ComboBox model)
    {
        var group = Guid.NewGuid().ToString("N");
        for (var i = 0; i < radios.Length; i++)
        {
            var index = i;
            radios[i].GroupName = group;
            radios[i].IsChecked = model.SelectedIndex == i;
            radios[i].IsCheckedChanged += (_, _) =>
            {
                if (!_updating && radios[index].IsChecked == true) model.SelectedIndex = index;
            };
            AutomationProperties.SetName(radios[i], radios[i].Content?.ToString());
        }
    }

    private void SyncLayoutState()
    {
        if (_rangeRadios is null || _refresh is null) return;
        _updating = true;
        try
        {
            foreach (var (radios, model) in new[]
                         { (_rangeRadios, _rangeMode), (_orientationRadios, _orientation), (_colorRadios, _color) })
                for (var i = 0; i < radios.Length; i++)
                    radios[i].IsChecked = i == model.SelectedIndex;
            foreach (var radio in _rangeRadios.Concat(_orientationRadios)) radio.IsEnabled = !IsBusy;
            _colorRadios[0].IsEnabled = !IsBusy;
            _colorRadios[1].IsEnabled = _color.IsEnabled;
            _refresh.IsVisible = _capabilities is null;
            _copySpinner.IsEnabled = !IsBusy;
            _thumbnails.SelectedIndex = _page;
        }
        finally
        {
            _updating = false;
        }
    }

    private void ArrangeWorkspace()
    {
        _narrow = Bounds.Width < 900;
        var showThumbnails = Bounds.Width >= 1160;
        _mobileTab.IsVisible = _narrow;
        _branding.IsVisible = !_narrow;
        _subtitle.IsVisible = Bounds.Width >= 1000;
        var columns = _narrow ? "*" : showThumbnails ? "220,*,364" : "*,340";
        if (_workspaceColumns != columns)
        {
            _workspaceColumns = columns;
            _body.ColumnDefinitions = new ColumnDefinitions(columns);
            // Invalidate the cell's previously clamped index when the column count changes.
            Grid.SetColumn(_settingsFrame, showThumbnails ? 0 : 2);
        }

        _thumbnailFrame.IsVisible = showThumbnails;
        Grid.SetColumn(_previewFrame, showThumbnails ? 1 : 0);
        Grid.SetColumn(_settingsFrame, showThumbnails ? 2 : _narrow ? 0 : 1);
        _settingsFrame.IsVisible = !_narrow || _mobileTab.SelectedIndex == 0;
        _previewFrame.IsVisible = !_narrow || _mobileTab.SelectedIndex == 1;
        _footer.ColumnDefinitions = new ColumnDefinitions(_narrow ? "*" : "*,Auto");
        _footer.RowDefinitions = new RowDefinitions(_narrow ? "Auto,Auto" : "Auto");
        Grid.SetColumn(_footerActions, _narrow ? 0 : 1);
        Grid.SetRow(_footerActions, _narrow ? 1 : 0);
        _footerActions.Margin = _narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        _navigation.Spacing = _narrow ? 5 : 12;
    }

    private void Divider()
    {
        var line = new Border { Height = 1, Margin = new Thickness(0, 20, 0, 18) };
        line.Bind(Border.BackgroundProperty, new DynamicResourceExtension("PrintingTools.Line"));
        _settings.Children.Add(line);
    }

    private void AddSettingRow(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("104,*"), Margin = new Thickness(0, 0, 0, 20) };
        grid.Children.Add(Text(label));
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        _settings.Children.Add(grid);
    }

    private Control CreateThumbnail(int page)
    {
        var size = _thumbnailSettings is null
            ? ((float)Document.WidthPoints, (float)Document.HeightPoints)
            : PrintLayout.Size(_thumbnailSettings);
        var image = new Image
        {
            Name = "PrintThumbnailImage", Tag = page, Width = 148, Height = 148 * size.Item2 / size.Item1,
            Stretch = Stretch.Uniform
        };
        var paper = new Border { Child = image, Background = Brushes.White, BorderThickness = new Thickness(1) };
        paper.Classes.Add("print-thumb-sheet");
        var label = Text($"第 {page + 1} 页");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var result = new StackPanel { Spacing = 10, Children = { paper, label } };
        AutomationProperties.SetName(result, $"第 {page + 1} 页缩略图");
        // Start after attachment so a fast render cannot finish before its image is discoverable.
        image.AttachedToVisualTree += (_, _) =>
        {
            if (_thumbnailBitmaps.TryGetValue(page, out var bitmap)) image.Source = bitmap;
            else _ = RenderThumbnailAsync(page);
        };
        return result;
    }

    private void RefreshThumbnails(PrintSettings settings)
    {
        var normalized = settings with { Copies = 1, PageRange = "", Collate = true };
        if (_thumbnailSettings == normalized && _thumbnailMargins == _hardMargins) return;
        ClearThumbnails();
        _thumbnailSettings = normalized;
        _thumbnailMargins = _hardMargins;
        _updating = true;
        try
        {
            _thumbnails.ItemsSource = Enumerable.Range(0, PageCount).ToArray();
            _thumbnails.SelectedIndex = _page;
        }
        finally
        {
            _updating = false;
        }

        var size = PrintLayout.Size(normalized);
        foreach (var image in _thumbnails.GetVisualDescendants().OfType<Image>().Where(i => i.Tag is int))
        {
            image.Height = 148 * size.Height / size.Width;
            _ = RenderThumbnailAsync((int)image.Tag!);
        }
    }

    private async Task RenderThumbnailAsync(int page)
    {
        if (_thumbnailSettings is null || !_thumbnailPending.Add(page)) return;
        var generation = _thumbnailGeneration;
        var settings = _thumbnailSettings;
        var margins = _thumbnailMargins;
        try
        {
            var data = await Task.Run(() => PrintRendering.Preview(Document, settings, page, margins, 300));
            if (generation != _thumbnailGeneration || _lifetime is null) return;
            using var stream = new MemoryStream(data);
            var bitmap = new Bitmap(stream);
            _thumbnailBitmaps[page] = bitmap;
            foreach (var image in _thumbnails.GetVisualDescendants().OfType<Image>()
                         .Where(i => i.Tag is int p && p == page)) image.Source = bitmap;
            // Keep the cache bounded; recycled items can request their page again.
            while (_thumbnailBitmaps.Count > 24)
            {
                var key = _thumbnailBitmaps.Keys.OrderByDescending(p => Math.Abs(p - _page)).First();
                foreach (var image in _thumbnails.GetVisualDescendants().OfType<Image>()
                             .Where(i => i.Tag is int p && p == key)) image.Source = null;
                _thumbnailBitmaps.Remove(key, out var stale);
                stale?.Dispose();
            }
        }
        catch (Exception e)
        {
            if (generation == _thumbnailGeneration) _status.Text = "缩略图加载失败：" + e.Message;
        }
        finally
        {
            if (generation == _thumbnailGeneration) _thumbnailPending.Remove(page);
        }
    }

    private void ClearThumbnails()
    {
        ++_thumbnailGeneration;
        foreach (var image in _thumbnails.GetVisualDescendants().OfType<Image>()) image.Source = null;
        foreach (var bitmap in _thumbnailBitmaps.Values) bitmap.Dispose();
        _thumbnailBitmaps.Clear();
        _thumbnailPending.Clear();
        _thumbnailSettings = null;
        _thumbnailMargins = null;
    }

    private static PathIcon Icon(string path, double size = 16)
    {
        return new PathIcon
        {
            Data = Geometry.Parse(path), Width = size, Height = size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
    }

    private sealed class PrintingMark : Control
    {
        private static readonly Geometry One = Geometry.Parse("M208,208 L264,164 H300 V360 H248 V228 L208,258 Z");

        public override void Render(DrawingContext context)
        {
            var accent = new SolidColorBrush(Color.Parse("#006061"));
            var white = new SolidColorBrush(Color.Parse("#F7F8F8"));
            using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 512, Bounds.Height / 512)))
            {
                context.DrawRectangle(accent, null, new Rect(0, 0, 512, 512), 36, 36);
                context.DrawRectangle(white, null, new Rect(112, 104, 288, 304), 48, 48);
                context.DrawRectangle(accent, null, new Rect(160, 152, 192, 208), 24, 24);
                context.DrawGeometry(white, null, One);
            }
        }
    }
}
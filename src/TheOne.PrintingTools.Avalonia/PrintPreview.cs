using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace TheOne.PrintingTools.Avalonia;

/// <summary>Embeddable print preview. The caller owns Document and must keep it alive while attached.</summary>
public sealed partial class PrintPreview : UserControl
{
    public static readonly StyledProperty<PrintDensity> DensityProperty =
        AvaloniaProperty.Register<PrintPreview, PrintDensity>(nameof(Density), PrintDensity.Normal);

    private readonly Grid _body = new() { ColumnDefinitions = new ColumnDefinitions("220,*,364") };
    private readonly Button _cancel = new() { Name = "PrintCancel", Content = "取消" };
    private readonly CheckBox _collate = new() { Content = "逐份打印", IsChecked = true };

    private readonly ComboBox _color = new()
        { Name = "PrintColor", ItemsSource = new[] { "黑白", "彩色" }, SelectedIndex = 1 };

    private readonly NumericUpDown _copies = new()
        { Name = "PrintCopies", Minimum = 1, Maximum = 999, Value = 1, Increment = 1, FormatString = "0" };

    private readonly ComboBox _duplex = new()
        { Name = "PrintDuplex", ItemsSource = new[] { "单面", "长边翻转", "短边翻转" }, SelectedIndex = 0 };

    private readonly Button _export = new() { Name = "PrintExport", Content = "导出 PDF" };
    private readonly Grid _footer = new() { ColumnDefinitions = new ColumnDefinitions("340,*") };
    private readonly Image _image = new() { Name = "PrintSheet", Stretch = Stretch.Uniform };
    private readonly List<Control> _inputs = [];

    private readonly NumericUpDown _margin = new()
        { Name = "PrintMargin", Minimum = 0, Maximum = 200, Value = 10, Increment = 1 };

    private readonly ComboBox _mobileTab = new()
        { Name = "PrintNarrowTab", ItemsSource = new[] { "打印设置", "页面预览" }, SelectedIndex = 0, IsVisible = false };

    private readonly Button _next = new() { Name = "PrintNext" };

    private readonly ComboBox _orientation = new()
        { Name = "PrintOrientation", ItemsSource = new[] { "纵向", "横向" }, SelectedIndex = 0 };

    private readonly TextBlock _pageLabel = Text("");
    private readonly ComboBox _paper = new() { Name = "PrintPaper" };

    private readonly NumericUpDown _percent = new()
        { Name = "PrintScalePercent", Minimum = 10, Maximum = 400, Value = 100, Increment = 5, IsVisible = false };

    private readonly Button _previous = new() { Name = "PrintPrevious" };

    private readonly Button _print = new()
        { Name = "PrintSubmit", Content = "打印", MinWidth = 82, IsEnabled = false, IsDefault = true };

    private readonly TextBlock _printerState = Text("正在发现打印机…", true);

    private readonly ComboBox _printers = new() { Name = "PrintPrinter" };

    private readonly TextBox _range = new()
        { Name = "PrintPageRange", PlaceholderText = "例如：1-3,5", IsVisible = false };

    private readonly ComboBox _rangeMode = new()
        { Name = "PrintRangeMode", ItemsSource = new[] { "全部页面", "当前页", "自定义页码" }, SelectedIndex = 0 };

    private readonly int _rasterDpi;
    private readonly string? _requestedPrinterId;

    private readonly ComboBox _scale = new()
        { Name = "PrintScale", ItemsSource = new[] { "适合可打印区域", "实际大小（可能裁切）", "自定义比例" }, SelectedIndex = 0 };

    private readonly StackPanel _settings = new() { Spacing = 17, Margin = new Thickness(24, 20) };
    private readonly TextBlock _status = Text("", true);
    private readonly TextBlock _summary = Text("");

    private readonly ComboBox _zoom = new()
    {
        Name = "PrintZoom", ItemsSource = new[] { "适合页面", "50%", "75%", "100%", "125%", "150%" }, SelectedIndex = 0,
        MinWidth = 110
    };

    private Bitmap? _bitmap;
    private PrinterCapabilities? _capabilities;
    private PrintMargins? _hardMargins;
    private PrintMargins? _initialMargins;
    private CancellationTokenSource? _lifetime, _operation;
    private bool _loadingMargins, _marginsValid;
    private string? _marginError;
    private string? _marginKey;
    private StackPanel _navigation = null!;
    private int _page, _refreshGeneration, _selectionGeneration, _renderGeneration;
    private ScrollViewer _previewScroll = null!;
    private Border _settingsFrame = null!, _previewFrame = null!, _sheet = null!;
    private bool _updating, _loading, _closeWhenIdle, _narrow;

    public PrintPreview(PrintDocument document, PrintService? service = null, PrintSettings? settings = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Service = service ?? Printing.Default;
        if (settings is not null) PrintLayout.Validate(document, settings);
        _requestedPrinterId = settings?.PrinterId;
        _rasterDpi = settings?.RasterDpi ?? 300;
        InitializeImageOptions(settings?.ImageOptions ?? new PrintImageOptions());
        Bind(BackgroundProperty, new DynamicResourceExtension("PrintingTools.Surface"));
        Bind(ForegroundProperty, new DynamicResourceExtension("PrintingTools.Text"));
        _printers.ItemTemplate = new FuncDataTemplate<PrintPrinter>((p, _) =>
        {
            var label = Text(p?.Name ?? "");
            label.MaxWidth = 230;
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            return new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
                Children = { Icon(PrinterPath, 18), label }
            };
        });
        _paper.ItemTemplate = new FuncDataTemplate<PrintPaper>((p, _) => Text(p?.ToString() ?? ""));
        _paper.ItemsSource = new[] { settings?.Paper ?? document.Paper };
        _paper.SelectedIndex = 0;
        if (settings is not null)
        {
            _copies.Value = settings.Copies;
            _collate.IsChecked = settings.Collate;
            _orientation.SelectedIndex = (int)settings.Orientation;
            _color.SelectedIndex = (int)settings.Color;
            _duplex.SelectedIndex = (int)settings.Duplex;
            _scale.SelectedIndex = (int)settings.ScaleMode;
            _percent.Value = (decimal)settings.ScalePercent;
            _margin.Value = (decimal)settings.Margins.Left;
            _initialMargins = settings.Margins;
            _range.Text = settings.PageRange;
            _rangeMode.SelectedIndex = string.IsNullOrWhiteSpace(settings.PageRange) ? 0 : 2;
        }

        _print.Classes.Add("print-primary");
        var refresh = BuildLayout();
        refresh.Click += async (_, _) => await RefreshPrintersAsync();
        _inputs.AddRange([
            _printers, refresh, _rangeMode, _range, _copies, _collate, _paper, _orientation, _color, _duplex, _scale,
            _percent, _margin
        ]);
        WireImageOptions();
        _printers.SelectionChanged += async (_, _) =>
        {
            if (!_updating) await LoadCapabilitiesAsync();
        };
        foreach (var input in new[] { _rangeMode, _paper, _orientation, _color, _duplex, _scale })
            input.SelectionChanged += (_, _) => Changed();
        _range.TextChanged += (_, _) => Changed();
        _range.GotFocus += (_, _) => _rangeMode.SelectedIndex = 2;
        foreach (var input in new[] { _copies, _percent }) input.ValueChanged += (_, _) => Changed();
        _margin.ValueChanged += (_, _) =>
        {
            _initialMargins = null;
            Changed();
        };
        _collate.IsCheckedChanged += (_, _) => Changed();
        _previous.Click += (_, _) => GoToPage(_page);
        _next.Click += (_, _) => GoToPage(_page + 2);
        _zoom.SelectionChanged += (_, _) => FitSheet();
        _mobileTab.SelectionChanged += (_, _) => ArrangeResponsive();
        _print.Click += async (_, _) => await SubmitAsync();
        _export.Click += async (_, _) => await ExportWithPickerAsync();
        _cancel.Click += (_, _) => Cancel();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Cancel();
                e.Handled = true;
            }
        };
        SizeChanged += (_, _) =>
        {
            ArrangeResponsive();
            FitSheet();
        };
        _previewScroll.SizeChanged += (_, _) => FitSheet();
        AttachedToVisualTree += async (_, _) =>
        {
            _lifetime = new CancellationTokenSource();
            ApplyDensity();
            Changed();
            await RefreshPrintersAsync();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            _operation?.Cancel();
            ++_renderGeneration;
            _image.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            ClearThumbnails();
        };
        foreach (var (control, label) in new (Control, string)[]
                 {
                     (_printers, "打印机"), (_rangeMode, "打印范围"), (_range, "自定义页码"), (_copies, "份数"), (_paper, "纸张尺寸"),
                     (_orientation, "纸张方向"), (_color, "颜色"), (_duplex, "双面打印"), (_margin, "页边距毫米"), (_scale, "打印缩放"),
                     (_percent, "缩放百分比"), (_zoom, "预览缩放"), (_previous, "上一页"), (_next, "下一页")
                 })
            AutomationProperties.SetName(control, label);
    }

    public PrintDensity Density
    {
        get => GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    public PrintDocument Document { get; }
    public PrintService Service { get; }
    public bool IsBusy { get; private set; }
    public int CurrentPage => _page + 1;
    public PrintResult? LastResult { get; private set; }
    public event EventHandler<PrintResult>? CloseRequested;

    public async Task RefreshPrintersAsync()
    {
        if (IsBusy) return;
        var generation = ++_refreshGeneration;
        ++_selectionGeneration;
        _loading = true;
        _capabilities = null;
        _marginKey = null;
        _hardMargins = null;
        _marginsValid = false;
        _loadingMargins = false;
        _marginError = null;
        _printerState.Text = "正在发现打印机…";
        Changed();
        var token = _lifetime?.Token ?? CancellationToken.None;
        try
        {
            var previous = (_printers.SelectedItem as PrintPrinter)?.Id ?? _requestedPrinterId;
            var printers = await Service.GetPrintersAsync(token);
            if (generation != _refreshGeneration || token.IsCancellationRequested) return;
            _updating = true;
            try
            {
                _printers.ItemsSource = printers;
                _printers.SelectedItem = previous is not null
                    ? printers.FirstOrDefault(p => p.Id == previous)
                    : printers.FirstOrDefault(p => p.IsDefault) ?? printers.FirstOrDefault();
            }
            finally
            {
                _updating = false;
            }

            if (printers.Count == 0)
            {
                _loading = false;
                _printerState.Text = "未发现打印机，仍可预览和导出 PDF。";
                Changed();
            }
            else if (_printers.SelectedItem is null)
            {
                _loading = false;
                _printerState.Text = "指定的打印机不可用，请重新选择设备。";
                Changed();
            }
            else
            {
                await LoadCapabilitiesAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (generation == _refreshGeneration)
            {
                _loading = false;
                _printerState.Text = e.Message;
                Changed();
            }
        }
    }

    private async Task LoadCapabilitiesAsync()
    {
        var generation = ++_selectionGeneration;
        _capabilities = null;
        _marginKey = null;
        _hardMargins = null;
        _marginsValid = false;
        _loadingMargins = false;
        _marginError = null;
        if (_printers.SelectedItem is not PrintPrinter printer)
        {
            _loading = false;
            Changed();
            return;
        }

        _loading = true;
        _printerState.Text = "正在读取打印机能力…";
        Changed();
        var token = _lifetime?.Token ?? CancellationToken.None;
        try
        {
            var capabilities = await Service.GetCapabilitiesAsync(printer.Id, token);
            if (generation != _selectionGeneration || token.IsCancellationRequested) return;
            _capabilities = capabilities;
            _updating = true;
            try
            {
                var previous = _paper.SelectedItem as PrintPaper;
                _paper.ItemsSource = capabilities.Papers;
                _paper.SelectedItem =
                    capabilities.Papers.FirstOrDefault(p =>
                        previous is not null && Math.Abs(p.WidthMm - previous.WidthMm) < 1 &&
                        Math.Abs(p.HeightMm - previous.HeightMm) < 1) ?? capabilities.Papers.FirstOrDefault();
                if (!capabilities.SupportsColor) _color.SelectedIndex = 0;
                var selectedDuplex = _duplex.SelectedIndex;
                _duplex.ItemsSource = new[] { "单面", "长边翻转", "短边翻转" }.Select((label, i) => new ComboBoxItem
                    { Content = label, IsEnabled = capabilities.DuplexModes.Contains((PrintDuplex)i) }).ToArray();
                _duplex.SelectedIndex =
                    capabilities.DuplexModes.Contains((PrintDuplex)selectedDuplex) ? selectedDuplex : 0;
            }
            finally
            {
                _updating = false;
            }

            _printerState.Text = capabilities.IsEstimated ? "纸张能力未知，请确认设备设置。" : printer.Status;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (generation == _selectionGeneration) _printerState.Text = e.Message;
        }
        finally
        {
            if (generation == _selectionGeneration)
            {
                _loading = false;
                Changed();
            }
        }
    }

    public PrintSettings GetSettings()
    {
        var margin = (double)(_margin.Value ?? throw new ArgumentException("请输入页边距。"));
        var settings = new PrintSettings
        {
            PrinterId = (_printers.SelectedItem as PrintPrinter)?.Id,
            PageRange = _rangeMode.SelectedIndex == 1 ? CurrentPage.ToString() :
                _rangeMode.SelectedIndex == 2 ? _range.Text ?? "" : "",
            Copies = checked((int)(_copies.Value ?? throw new ArgumentException("请输入份数。"))),
            Collate = _collate.IsChecked == true,
            Paper = _paper.SelectedItem as PrintPaper ?? Document.Paper,
            Orientation = (PrintOrientation)_orientation.SelectedIndex,
            Color = (PrintColor)_color.SelectedIndex, Duplex = (PrintDuplex)_duplex.SelectedIndex,
            ScaleMode = (PrintScaleMode)_scale.SelectedIndex, ScalePercent = (double)(_percent.Value ?? 100),
            Margins = _initialMargins ?? new PrintMargins(margin, margin, margin, margin), RasterDpi = _rasterDpi,
            ImageOptions = GetImageOptions()
        };
        if (_copies.Value != decimal.Truncate(_copies.Value ?? 0)) throw new ArgumentException("份数必须为整数。");
        if (_rangeMode.SelectedIndex == 2 && string.IsNullOrWhiteSpace(_range.Text))
            throw new ArgumentException("请输入自定义页码。");
        PrintLayout.Validate(Document, settings);
        return settings;
    }

    public void GoToPage(int oneBasedPage)
    {
        _page = Math.Clamp(oneBasedPage - 1, 0, PageCount - 1);
        Changed();
    }

    public void Cancel()
    {
        if (IsBusy)
        {
            _closeWhenIdle = true;
            _operation?.Cancel();
            _status.Text = "正在请求取消，请等待系统返回；已输出页面可能无法撤回。";
        }
        else
        {
            CloseRequested?.Invoke(this, PrintResult.Cancelled);
        }
    }

    private void Changed()
    {
        if (_updating) return;
        _range.IsEnabled = !IsBusy;
        _percent.IsVisible = _scale.SelectedIndex == 2;
        _page = Math.Clamp(_page, 0, PageCount - 1);
        _previous.IsEnabled = _page > 0;
        _next.IsEnabled = _page + 1 < PageCount;
        _pageLabel.Text = $"第 {CurrentPage} / {PageCount} 页";
        _totalLabel.Text = $"共 {PageCount} 页";
        _subtitle.Text = $"{Document.Title} · {PageCount} 页";
        _color.IsEnabled = !IsBusy && (_capabilities is null || _capabilities.SupportsColor);
        _duplex.IsEnabled = !IsBusy && _capabilities?.SupportsDuplex == true;
        ToolTip.SetTip(_color, _color.IsEnabled ? "按设备支持选择颜色" : "设备未报告彩色支持");
        ToolTip.SetTip(_duplex, _duplex.IsEnabled ? "按设备支持选择单双面" : "设备未报告双面支持");
        SyncLayoutState();
        try
        {
            var settings = GetSettings();
            var count = PrintPageRange.Parse(settings.PageRange, PageCount).Count;
            if (_capabilities is not null && settings.PrinterId is not null)
            {
                var key =
                    $"{settings.PrinterId}|{settings.Paper}|{settings.Paper.NativeId}|{settings.Orientation}|{settings.Duplex}|{settings.Color}";
                if (_marginKey != key)
                {
                    _marginKey = key;
                    _loadingMargins = true;
                    _marginsValid = false;
                    _marginError = null;
                    _ = LoadPageMarginsAsync(settings, _capabilities, key);
                }
            }

            _summary.Text = $"已选 {count} 页 · {settings.Copies} 份";
            _print.IsEnabled = !IsBusy && !_loading && !_loadingMargins && _marginsValid && _capabilities is not null &&
                               settings.PrinterId is not null;
            _export.IsEnabled = !IsBusy && !_loadingMargins;
            if (!IsBusy)
                _status.Text = _marginError ?? (_loadingMargins ? "正在读取可打印区域…" :
                    settings.ScaleMode == PrintScaleMode.Fit ? "" : "实际大小或自定义比例可能在不可打印边距处裁切。");
            RefreshThumbnails(settings);
            _ = RenderPreviewAsync(settings);
        }
        catch (Exception e)
        {
            _status.Text = e.Message;
            _print.IsEnabled = false;
            _export.IsEnabled = false;
        }
    }

    private async Task LoadPageMarginsAsync(PrintSettings settings, PrinterCapabilities capabilities, string key)
    {
        var token = _lifetime?.Token ?? CancellationToken.None;
        var generation = _selectionGeneration;
        try
        {
            var margins = await Service.GetPageMarginsAsync(settings, capabilities, token);
            if (_marginKey != key || generation != _selectionGeneration || token.IsCancellationRequested) return;
            _hardMargins = margins;
            _marginsValid = true;
            _loadingMargins = false;
            Changed();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (_marginKey == key && generation == _selectionGeneration)
            {
                _loadingMargins = false;
                _marginsValid = false;
                _marginError = e.Message;
                Changed();
            }
        }
    }

    private async Task RenderPreviewAsync(PrintSettings settings)
    {
        var generation = ++_renderGeneration;
        var page = _page;
        try
        {
            var hard = _hardMargins;
            var data = await Task.Run(() => PrintRendering.Preview(Document, settings, page, hard));
            if (generation != _renderGeneration || _lifetime is null) return;
            using var stream = new MemoryStream(data);
            var bitmap = new Bitmap(stream);
            _image.Source = bitmap;
            _bitmap?.Dispose();
            _bitmap = bitmap;
            _renderedSize = PrintLayout.Size(settings);
            FitSheet();
        }
        catch (Exception e)
        {
            if (generation == _renderGeneration && _lifetime is not null) _status.Text = "预览失败：" + e.Message;
        }
    }

    private void FitSheet()
    {
        if (_bitmap is null) return;
        var sourceWidth = _renderedSize.Width * 96 / 72;
        var sourceHeight = _renderedSize.Height * 96 / 72;
        var scale = _zoom.SelectedIndex switch
        {
            1 => .5, 2 => .75, 3 => 1, 4 => 1.25, 5 => 1.5,
            _ => Math.Min(Math.Max(100, _previewScroll.Bounds.Width - 64) / sourceWidth,
                Math.Max(100, _previewScroll.Bounds.Height - 64) / sourceHeight)
        };
        _image.Width = sourceWidth * scale;
        _image.Height = sourceHeight * scale;
    }

    private void ArrangeResponsive()
    {
        ArrangeWorkspace();
    }

    private async Task SubmitAsync()
    {
        if (IsBusy || !_print.IsEnabled) return;
        try
        {
            var settings = GetSettings();
            BeginOperation();
            _status.Text = "正在提交打印任务…";
            LastResult = await Service.SubmitAsync(Document, settings, _operation!.Token);
            _status.Text = LastResult.Message;
        }
        catch (OperationCanceledException)
        {
            LastResult = PrintResult.Cancelled;
            _status.Text = "已取消尚未提交的打印操作。";
        }
        catch (Exception e)
        {
            LastResult = null;
            _status.Text = e.Message;
        }
        finally
        {
            EndOperation();
        }

        if (LastResult is { IsCancelled: false } || _closeWhenIdle)
        {
            _closeWhenIdle = false;
            CloseRequested?.Invoke(this, LastResult ?? PrintResult.Cancelled);
        }
    }

    private async Task ExportWithPickerAsync()
    {
        if (IsBusy) return;
        try
        {
            var settings = GetSettings();
            BeginOperation();
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage?.CanSave != true) throw new PrintException("当前宿主不支持保存文件对话框。");
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出 PDF", SuggestedFileName = Document.Title + ".pdf", DefaultExtension = "pdf",
                FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            var hard = _hardMargins ?? PrintMargins.Zero;
            var m = settings.Margins;
            await Service.ExportPdfAsync(Document, stream,
                settings with
                {
                    Margins = new PrintMargins(Math.Max(m.Left, hard.Left), Math.Max(m.Top, hard.Top),
                        Math.Max(m.Right, hard.Right), Math.Max(m.Bottom, hard.Bottom))
                }, _operation!.Token);
            _status.Text = "PDF 已导出，包含所选页面各一份。";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "已取消导出，目标文件可能不完整。";
        }
        catch (Exception e)
        {
            _status.Text = e.Message;
        }
        finally
        {
            if (IsBusy) EndOperation();
            if (_closeWhenIdle)
            {
                _closeWhenIdle = false;
                CloseRequested?.Invoke(this, LastResult ?? PrintResult.Cancelled);
            }
        }
    }

    private void BeginOperation()
    {
        IsBusy = true;
        LastResult = null;
        _operation = new CancellationTokenSource();
        foreach (var input in _inputs) input.IsEnabled = false;
        _print.IsEnabled = _export.IsEnabled = false;
        _cancel.Content = "取消操作";
    }

    private void EndOperation()
    {
        IsBusy = false;
        _operation?.Dispose();
        _operation = null;
        foreach (var input in _inputs) input.IsEnabled = true;
        var message = _status.Text;
        Changed();
        _status.Text = message;
        _cancel.Content = "取消";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_previewScroll.IsKeyboardFocusWithin && !(_narrow && _mobileTab.SelectedIndex == 1)) return;
        if (e.Key == Key.PageDown)
        {
            GoToPage(CurrentPage + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp)
        {
            GoToPage(CurrentPage - 1);
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DensityProperty && _inputs is not null) ApplyDensity();
    }

    private void ApplyDensity()
    {
        var height = Density switch { PrintDensity.Small => 34, PrintDensity.Large => 46, _ => 40 };
        FontSize = Density == PrintDensity.Small ? 12 : Density == PrintDensity.Large ? 16 : 14;
        foreach (var c in _inputs.Concat<Control>([_print, _export, _cancel, _previous, _next, _zoom, _mobileTab])
                     .OfType<TemplatedControl>()) c.FontSize = FontSize;
        foreach (var radio in _rangeRadios.Concat(_orientationRadios).Concat(_colorRadios)) radio.FontSize = FontSize;
        foreach (var c in _inputs.Concat([_print, _export, _cancel, _previous, _next, _zoom])) c.MinHeight = height;
        foreach (var button in new[] { _print, _export, _cancel })
        {
            button.MinHeight = height + 2;
            button.Height = height + 4;
        }

        foreach (var button in new[] { _previous, _next, _zoomOut, _zoomIn })
        {
            button.MinHeight = 0;
            button.Width = height - 4;
            button.Height = height - 4;
        }

        _zoom.MinHeight = height - 4;
        _zoom.Height = height - 4;
        if (_copySpinner is not null) _copySpinner.Height = height;
    }

    private static TextBlock Text(string text, bool muted = false)
    {
        var result = new TextBlock
            { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        result.Bind(TextBlock.ForegroundProperty,
            new DynamicResourceExtension(muted ? "PrintingTools.Muted" : "PrintingTools.Text"));
        return result;
    }

    private static Border Frame(Control child, string background)
    {
        var frame = new Border { Child = child };
        frame.Bind(Border.BackgroundProperty, new DynamicResourceExtension(background));
        frame.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("PrintingTools.Line"));
        return frame;
    }
}
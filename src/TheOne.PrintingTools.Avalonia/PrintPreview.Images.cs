using global::Avalonia.Controls;
using global::Avalonia.Layout;

namespace TheOne.PrintingTools.Avalonia;

public sealed partial class PrintPreview
{
    private readonly ComboBox _imageLayout = new() { Name = "PrintImageLayout", ItemsSource = new[] { "每页一张", "上下两张", "左右两张", "四宫格", "九宫格" }, SelectedIndex = 0 };
    private readonly ComboBox _imageFill = new() { Name = "PrintImageFill", ItemsSource = new[] { "完整显示", "铺满裁切", "拉伸", "实际大小（96 DPI）" }, SelectedIndex = 0 };
    private readonly ComboBox _imageAlignment = new() { Name = "PrintImageAlignment", ItemsSource = new[] { "左上", "居上", "右上", "居左", "居中", "居右", "左下", "居下", "右下" }, SelectedIndex = 4 };
    private readonly ComboBox _imageOrder = new() { Name = "PrintImageOrder", ItemsSource = new[] { "先横后纵", "先纵后横" }, SelectedIndex = 0 };
    private readonly NumericUpDown _imageSpacing = new() { Name = "PrintImageSpacing", Minimum = 0, Maximum = 100, Value = 4, Increment = 1 };
    private readonly CheckBox _imageRotate = new() { Name = "PrintImageAutoRotate", Content = "自动旋转以适合单元格", IsChecked = false };
    private PrintImageOptions _initialImageOptions = new();

    public int PageCount
    {
        get
        {
            if (!Document.IsImageDocument) return Document.PageCount;
            var (rows, columns) = ImagePrintDocument.Grid((PrintImageLayout)Math.Max(0, _imageLayout.SelectedIndex));
            return (Document.PageCount + rows * columns - 1) / (rows * columns);
        }
    }

    private void InitializeImageOptions(PrintImageOptions options)
    {
        _initialImageOptions = options;
        if (!Document.IsImageDocument) return;
        _imageLayout.SelectedIndex = (int)options.Layout; _imageFill.SelectedIndex = (int)options.Fill;
        _imageAlignment.SelectedIndex = (int)options.Alignment; _imageOrder.SelectedIndex = (int)options.Order;
        _imageSpacing.Value = (decimal)options.SpacingMm; _imageRotate.IsChecked = options.AutoRotate;
    }

    private PrintImageOptions GetImageOptions() => !Document.IsImageDocument ? _initialImageOptions : new()
    {
        Layout = (PrintImageLayout)_imageLayout.SelectedIndex, Fill = (PrintImageFill)_imageFill.SelectedIndex,
        Alignment = (PrintImageAlignment)_imageAlignment.SelectedIndex, Order = (PrintImageOrder)_imageOrder.SelectedIndex,
        SpacingMm = (double)(_imageSpacing.Value ?? throw new ArgumentException("请输入图片间距。")), AutoRotate = _imageRotate.IsChecked == true
    };

    private void AddImageOptions()
    {
        if (!Document.IsImageDocument) return;
        Divider();
        var title = Text("图片内容与排版"); title.FontWeight = global::Avalonia.Media.FontWeight.SemiBold;
        title.Margin = new global::Avalonia.Thickness(0, 0, 0, 12); _settings.Children.Add(title);
        AddSettingRow("内容布局", _imageLayout); AddSettingRow("填充方式", _imageFill);
        AddSettingRow("内容对齐", _imageAlignment); AddSettingRow("排列顺序", _imageOrder);
        AddSettingRow("间距 mm", _imageSpacing);
        _imageRotate.HorizontalAlignment = HorizontalAlignment.Left; _settings.Children.Add(_imageRotate);
    }

    private void WireImageOptions()
    {
        if (!Document.IsImageDocument) return;
        _inputs.AddRange([_imageLayout, _imageFill, _imageAlignment, _imageOrder, _imageSpacing, _imageRotate]);
        foreach (var input in new[] { _imageLayout, _imageFill, _imageAlignment, _imageOrder }) input.SelectionChanged += (_, _) => Changed();
        _imageSpacing.ValueChanged += (_, _) => Changed();
        _imageRotate.IsCheckedChanged += (_, _) => Changed();
        foreach (var (control, name) in new (Control, string)[] { (_imageLayout, "图片内容布局"), (_imageFill, "图片填充方式"),
                     (_imageAlignment, "图片内容对齐"), (_imageOrder, "图片排列顺序"), (_imageSpacing, "图片间距毫米"), (_imageRotate, "图片自动旋转") })
            global::Avalonia.Automation.AutomationProperties.SetName(control, name);
    }
}

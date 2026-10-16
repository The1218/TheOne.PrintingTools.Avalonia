using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TheOne.PrintingTools.Avalonia;

namespace TheOne.DesignPreview;

public interface IPrintingDemoPlatform
{
    Task<PrintSource?> PickSourceAsync(bool allowMultiple);
    Task<PrintSource?> PickDocumentAsync();
    PrintSource CreateVisualSource();
    PrintSource CreateMixedSource();
}

// The ViewModel only chooses a source and calls the injected printing service.
public sealed class PrintingDemoViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IPrintingDemoPlatform _platform;
    private readonly IPrintService _printing;
    private readonly DemoCommand[] _commands;
    private CancellationTokenSource? _operation;
    private PrintSource _source = SampleReport.CreateSource();
    private string _status = "选择打印源后点击打印，默认先打开预览。", _printerId = "";
    private bool _isBusy, _disposed, _showPreview = true;
    private decimal _copies = 1;

    public PrintingDemoViewModel(IPrintingDemoPlatform platform, IPrintService printing)
    {
        _platform = platform;
        _printing = printing;
        SingleFileCommand = new DemoCommand(() => PickAsync(false), CanRun);
        MultipleFilesCommand = new DemoCommand(() => PickAsync(true), CanRun);
        DocumentFileCommand = new DemoCommand(() => PickAsync(false, documents: true), CanRun);
        VisualCommand = new DemoCommand(() => SelectAsync(_platform.CreateVisualSource()), CanRun);
        SampleCommand = new DemoCommand(() => SelectAsync(SampleReport.CreateSource()), CanRun);
        MixedCommand = new DemoCommand(() => SelectAsync(_platform.CreateMixedSource()), CanRun);
        PrintCommand = new DemoCommand(PrintAsync, CanRun);
        CancelCommand = new DemoCommand(() => { Cancel(); return Task.CompletedTask; }, () => IsBusy);
        _commands = [SingleFileCommand, MultipleFilesCommand, DocumentFileCommand, VisualCommand, SampleCommand, MixedCommand, PrintCommand];
        foreach (var command in _commands) command.Run = RunAsync;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public PrintSource Source => _source;
    public string Summary => Source.Title;
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public bool IsBusy => _isBusy;
    public bool ShowPreview { get => _showPreview; set { _showPreview = value; Changed(); Changed(nameof(CallExample)); } }
    public decimal Copies { get => _copies; set { _copies = value; Changed(); } }
    public string PrinterId { get => _printerId; set { _printerId = value; Changed(); } }
    public string CallExample => $"await _printing.PrintAsync(source, options, showPreview: {ShowPreview.ToString().ToLowerInvariant()});";
    public DemoCommand SingleFileCommand { get; }
    public DemoCommand MultipleFilesCommand { get; }
    public DemoCommand DocumentFileCommand { get; }
    public DemoCommand VisualCommand { get; }
    public DemoCommand SampleCommand { get; }
    public DemoCommand MixedCommand { get; }
    public DemoCommand PrintCommand { get; }
    public DemoCommand CancelCommand { get; }

    private bool CanRun() => !_disposed && !IsBusy;
    private async Task PickAsync(bool multiple, bool documents = false)
    {
        var source = documents ? await _platform.PickDocumentAsync() : await _platform.PickSourceAsync(multiple);
        _operation!.Token.ThrowIfCancellationRequested();
        if (source is null) { Status = "已取消选择，保留当前打印源。"; return; }
        await SelectAsync(source);
    }
    private Task SelectAsync(PrintSource source)
    {
        _source = source;
        Changed(nameof(Source)); Changed(nameof(Summary));
        Status = "打印源已选择。点击打印后，由服务读取文件并处理预览或直接打印。";
        return Task.CompletedTask;
    }

    private async Task PrintAsync()
    {
        if (Copies != decimal.Truncate(Copies)) throw new ArgumentException("份数必须为整数。");
        var options = new PrintSettings { Copies = checked((int)Copies), PrinterId = string.IsNullOrWhiteSpace(PrinterId) ? null : PrinterId.Trim() };
        var result = await _printing.PrintAsync(Source, options, showPreview: ShowPreview, cancellationToken: _operation!.Token);
        Status = result.Message;
    }

    private async Task RunAsync(Func<Task> action)
    {
        _operation = new(); _isBusy = true; Changed(nameof(IsBusy)); RefreshCommands();
        try { await action(); }
        catch (OperationCanceledException) { Status = "操作已取消。"; }
        catch (Exception error) { Status = "操作失败：" + error.Message; }
        finally { _operation.Dispose(); _operation = null; _isBusy = false; Changed(nameof(IsBusy)); RefreshCommands(); }
    }
    public void Cancel() => _operation?.Cancel();
    private void RefreshCommands() { foreach (var command in _commands) command.Refresh(); CancelCommand.Refresh(); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose() { _disposed = true; Cancel(); RefreshCommands(); }
}

public sealed class DemoCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    internal Func<Func<Task>, Task> Run { get; set; } = action => action();
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) => await ExecuteAsync();
    public Task ExecuteAsync() => CanExecute(null) ? Run(execute) : Task.CompletedTask;
    internal void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

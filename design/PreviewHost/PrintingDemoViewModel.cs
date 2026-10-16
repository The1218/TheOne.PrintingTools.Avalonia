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
    private readonly DemoCommand[] _commands;
    private readonly IPrintingDemoPlatform _platform;
    private readonly IPrintService _printing;
    private decimal _copies = 1;
    private bool _disposed, _showPreview = true;
    private CancellationTokenSource? _operation;
    private string _status = "选择打印源后点击打印，默认先打开预览。", _printerId = "", _networkUrls = "";

    public PrintingDemoViewModel(IPrintingDemoPlatform platform, IPrintService printing)
    {
        _platform = platform;
        _printing = printing;
        SingleFileCommand = new DemoCommand(() => PickAsync(false), CanRun);
        MultipleFilesCommand = new DemoCommand(() => PickAsync(true), CanRun);
        DocumentFileCommand = new DemoCommand(() => PickAsync(false, true), CanRun);
        VisualCommand = new DemoCommand(() => SelectAsync(_platform.CreateVisualSource()), CanRun);
        SampleCommand = new DemoCommand(() => SelectAsync(SampleReport.CreateSource()), CanRun);
        MixedCommand = new DemoCommand(() => SelectAsync(_platform.CreateMixedSource()), CanRun);
        NetworkSourceCommand = new DemoCommand(SelectNetworkAsync, CanRun);
        PrintCommand = new DemoCommand(PrintAsync, CanRun);
        CancelCommand = new DemoCommand(() =>
        {
            Cancel();
            return Task.CompletedTask;
        }, () => IsBusy);
        _commands =
        [
            SingleFileCommand, MultipleFilesCommand, DocumentFileCommand, VisualCommand, SampleCommand, MixedCommand,
            NetworkSourceCommand, PrintCommand
        ];
        foreach (var command in _commands) command.Run = RunAsync;
    }

    public PrintSource Source { get; private set; } = SampleReport.CreateSource();

    public string Summary => Source.Title;

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Changed();
        }
    }

    public bool IsBusy { get; private set; }

    public bool ShowPreview
    {
        get => _showPreview;
        set
        {
            _showPreview = value;
            Changed();
            Changed(nameof(CallExample));
        }
    }

    public decimal Copies
    {
        get => _copies;
        set
        {
            _copies = value;
            Changed();
        }
    }

    public string PrinterId
    {
        get => _printerId;
        set
        {
            _printerId = value;
            Changed();
        }
    }

    public string NetworkUrls
    {
        get => _networkUrls;
        set
        {
            _networkUrls = value;
            Changed();
        }
    }

    public string CallExample =>
        $"await _printing.PrintAsync(source, options, showPreview: {ShowPreview.ToString().ToLowerInvariant()});";

    public DemoCommand SingleFileCommand { get; }
    public DemoCommand MultipleFilesCommand { get; }
    public DemoCommand DocumentFileCommand { get; }
    public DemoCommand VisualCommand { get; }
    public DemoCommand SampleCommand { get; }
    public DemoCommand MixedCommand { get; }
    public DemoCommand NetworkSourceCommand { get; }
    public DemoCommand PrintCommand { get; }
    public DemoCommand CancelCommand { get; }

    public void Dispose()
    {
        _disposed = true;
        Cancel();
        RefreshCommands();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool CanRun()
    {
        return !_disposed && !IsBusy;
    }

    private async Task PickAsync(bool multiple, bool documents = false)
    {
        var source = documents ? await _platform.PickDocumentAsync() : await _platform.PickSourceAsync(multiple);
        _operation!.Token.ThrowIfCancellationRequested();
        if (source is null)
        {
            Status = "已取消选择，保留当前打印源。";
            return;
        }

        await SelectAsync(source);
    }

    private Task SelectAsync(PrintSource source)
    {
        Source = source;
        Changed(nameof(Source));
        Changed(nameof(Summary));
        Status = "打印源已选择。点击打印后，由服务加载内容并处理预览或直接打印。";
        return Task.CompletedTask;
    }

    private Task SelectNetworkAsync()
    {
        var urls = NetworkUrls.Split(['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return SelectAsync(PrintSource.FromUrls(urls));
    }

    private async Task PrintAsync()
    {
        if (Copies != decimal.Truncate(Copies)) throw new ArgumentException("份数必须为整数。");
        var options = new PrintSettings
        {
            Copies = checked((int)Copies), PrinterId = string.IsNullOrWhiteSpace(PrinterId) ? null : PrinterId.Trim()
        };
        Status = "正在准备打印源；网络内容下载期间可取消操作。";
        var result = await _printing.PrintAsync(Source, options, showPreview: ShowPreview,
            cancellationToken: _operation!.Token);
        Status = result.Message;
    }

    private async Task RunAsync(Func<Task> action)
    {
        _operation = new CancellationTokenSource();
        IsBusy = true;
        Changed(nameof(IsBusy));
        RefreshCommands();
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            Status = "操作已取消。";
        }
        catch (Exception error)
        {
            Status = "操作失败：" + error.Message;
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            IsBusy = false;
            Changed(nameof(IsBusy));
            RefreshCommands();
        }
    }

    public void Cancel()
    {
        _operation?.Cancel();
    }

    private void RefreshCommands()
    {
        foreach (var command in _commands) command.Refresh();
        CancelCommand.Refresh();
    }

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class DemoCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    internal Func<Func<Task>, Task> Run { get; set; } = action => action();
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return canExecute();
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync();
    }

    public Task ExecuteAsync()
    {
        return CanExecute(null) ? Run(execute) : Task.CompletedTask;
    }

    internal void Refresh()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
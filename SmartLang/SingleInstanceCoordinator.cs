namespace SmartLang;

public sealed class SingleInstanceCoordinator: IDisposable {
    private const string MutexName = @"Local\SmartLang.Application";
    private const string OpenEventName = @"Local\SmartLang.OpenSettings";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _openEvent;
    private readonly EventWaitHandle _stopEvent = new(false, EventResetMode.ManualReset);
    private Thread? _listenerThread;

    public SingleInstanceCoordinator() {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _openEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            OpenEventName,
            out _);
    }

    public bool IsFirstInstance { get; }

    public void StartListening(Action openSettings) {
        if(!IsFirstInstance || _listenerThread is not null) {
            return;
        }

        _listenerThread = new Thread(() => {
            var handles = new WaitHandle[] { _openEvent, _stopEvent };
            while(WaitHandle.WaitAny(handles) == 0) {
                openSettings();
            }
        }) {
            IsBackground = true,
            Name = "SmartLang single-instance listener"
        };
        _listenerThread.Start();
    }

    public void SignalExistingInstance() {
        if(!IsFirstInstance) {
            _openEvent.Set();
        }
    }

    public void Dispose() {
        _stopEvent.Set();
        _listenerThread?.Join(TimeSpan.FromSeconds(1));
        _openEvent.Dispose();
        _stopEvent.Dispose();

        if(IsFirstInstance) {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}

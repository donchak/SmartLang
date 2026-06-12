namespace SmartLang;

public sealed class SingleInstanceCoordinator: IDisposable {
    const string MutexName = @"Local\SmartLang.Application";
    const string OpenEventName = @"Local\SmartLang.OpenSettings";

    readonly Mutex mutex;
    readonly EventWaitHandle openEvent;
    readonly EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset);
    Thread? listenerThread;

    public SingleInstanceCoordinator() {
        mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        openEvent = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEventName, out _);
    }

    public bool IsFirstInstance { get; }

    public void StartListening(Action openSettings) {
        if(!IsFirstInstance || listenerThread is not null) {
            return;
        }

        listenerThread = new Thread(() => {
            var handles = new WaitHandle[] { openEvent, stopEvent };
            while(WaitHandle.WaitAny(handles) == 0) {
                openSettings();
            }
        }) {
            IsBackground = true,
            Name = "SmartLang single-instance listener"
        };
        listenerThread.Start();
    }

    public void SignalExistingInstance() {
        if(!IsFirstInstance) {
            openEvent.Set();
        }
    }

    public void Dispose() {
        stopEvent.Set();
        listenerThread?.Join(TimeSpan.FromSeconds(1));
        openEvent.Dispose();
        stopEvent.Dispose();

        if(IsFirstInstance) {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
    }
}

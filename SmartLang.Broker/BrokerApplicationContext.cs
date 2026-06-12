using System.ComponentModel;

namespace SmartLang.Broker;

internal sealed class BrokerApplicationContext: ApplicationContext {
    readonly Control dispatcher = new();
    readonly SettingsStore settingsStore = new();
    readonly LanguageCatalog languageCatalog = new();
    readonly ScheduledTaskManager taskManager = new();
    readonly HookRuntimeController runtime;
    readonly BrokerPipeServer server;
    readonly System.Windows.Forms.Timer refreshTimer;

    AppSettings settings;
    string? lastError;
    bool isExiting;

    internal BrokerApplicationContext() {
        settings = settingsStore.Load();
        runtime = new HookRuntimeController(Dispatch, error => {  lastError = error;  AppLog.Write(error); });
        server = new BrokerPipeServer(HandleRequestAsync);
        refreshTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        refreshTimer.Tick += (_, _) => runtime.Refresh();

        dispatcher.CreateControl();
        dispatcher.BeginInvoke(Initialize);
    }

    protected override void Dispose(bool disposing) {
        if(disposing) {
            isExiting = true;
            refreshTimer.Stop();
            refreshTimer.Dispose();
            runtime.Dispose();
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            dispatcher.Dispose();
        }

        base.Dispose(disposing);
    }

    void Initialize() {
        var validation = SettingsValidator.Validate(settings, languageCatalog.GetLanguageOptions());
        if(validation is null) {
            TryActivateHooks();
        } else {
            lastError = validation;
        }

        server.Start();
        refreshTimer.Start();
    }

    Task<BrokerResponse> HandleRequestAsync(
        BrokerRequest request,
        CancellationToken cancellationToken) =>
        DispatchAsync(() => HandleRequest(request), cancellationToken);

    BrokerResponse HandleRequest(BrokerRequest request) {
        try {
            return request.Command switch {
                BrokerCommand.GetStatus => Success(request),
                BrokerCommand.ActivateHooks => ActivateHooks(request),
                BrokerCommand.SaveSettings => SaveSettings(request),
                BrokerCommand.ConfigureStartup => ConfigureStartup(request),
                BrokerCommand.Stop => Stop(request),
                _ => Failure(request, "Unsupported broker command.")
            };
        } catch(Exception exception) when(
              exception is IOException or UnauthorizedAccessException or
              InvalidOperationException or Win32Exception or
              System.Runtime.InteropServices.COMException) {
            lastError = exception.Message;
            AppLog.Write($"Broker command {request.Command} failed with " + $"{exception.GetType().Name}: {exception.Message}");
            return Failure(request, exception.Message);
        }
    }

    BrokerResponse ActivateHooks(BrokerRequest request) {
        var validation = SettingsValidator.Validate(settings, languageCatalog.GetLanguageOptions());
        if(validation is not null) {
            return Failure(request, validation);
        }

        return TryActivateHooks()
            ? Success(request)
            : Failure(request, "Another SmartLang process currently owns the input hooks.");
    }

    BrokerResponse SaveSettings(BrokerRequest request) {
        if(request.Settings is null) {
            return Failure(request, "Settings were not supplied.");
        }

        var validation = SettingsValidator.Validate(request.Settings, languageCatalog.GetLanguageOptions());
        if(validation is not null) {
            return Failure(request, validation);
        }

        settingsStore.Save(request.Settings);
        settings = request.Settings.Copy();
        lastError = null;
        if(!runtime.Restart(settings)) {
            lastError = "Another SmartLang process currently owns the input hooks.";
        }

        return Success(request);
    }

    BrokerResponse ConfigureStartup(BrokerRequest request) {
        var settings = request.Settings ?? this.settings;
        taskManager.Configure(settings.StartWithWindows, settings.AdministratorAppSupport);
        return Success(request);
    }

    BrokerResponse Stop(BrokerRequest request) {
        runtime.Stop();
        refreshTimer.Stop();
        _ = Task.Delay(250).ContinueWith(
            _ => Dispatch(() => {  if (!isExiting)  {   isExiting = true;   ExitThread();  } }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return Success(request);
    }

    bool TryActivateHooks() {
        try {
            var active = runtime.TryStart(settings);
            if(active) {
                lastError = null;
            }

            return active;
        } catch(Win32Exception exception) {
            lastError = exception.Message;
            AppLog.Write($"Broker could not activate hooks: {exception.Message}");
            return false;
        }
    }

    BrokerResponse Success(BrokerRequest request) =>
        new(BrokerProtocol.CurrentVersion, request.RequestId, true, GetStatus());

    BrokerResponse Failure(BrokerRequest request, string error) =>
        new(BrokerProtocol.CurrentVersion, request.RequestId, false, GetStatus(), error);

    BrokerStatus GetStatus() =>
        new(IsElevated: true, HooksActive: runtime.IsRunning, Version: Application.ProductVersion, LastError: lastError);

    Task<T> DispatchAsync<T>(Func<T> action, CancellationToken cancellationToken) {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        Dispatch(() => {
            try {
                completion.TrySetResult(action());
            } catch(Exception exception) {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    void Dispatch(Action action) {
        if(isExiting || dispatcher.IsDisposed) {
            return;
        }

        try {
            dispatcher.BeginInvoke(action);
        } catch(InvalidOperationException) when(isExiting || dispatcher.IsDisposed) {
        }
    }
}

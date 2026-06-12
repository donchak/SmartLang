using System.ComponentModel;

namespace SmartLang.Broker;

internal sealed class BrokerApplicationContext : ApplicationContext
{
    private readonly Control _dispatcher = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly LanguageCatalog _languageCatalog = new();
    private readonly ScheduledTaskManager _taskManager = new();
    private readonly HookRuntimeController _runtime;
    private readonly BrokerPipeServer _server;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private AppSettings _settings;
    private string? _lastError;
    private bool _isExiting;

    internal BrokerApplicationContext()
    {
        _settings = _settingsStore.Load();
        _runtime = new HookRuntimeController(
            Dispatch,
            error =>
            {
                _lastError = error;
                AppLog.Write(error);
            });
        _server = new BrokerPipeServer(HandleRequestAsync);
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _refreshTimer.Tick += (_, _) => _runtime.Refresh();

        _dispatcher.CreateControl();
        _dispatcher.BeginInvoke(Initialize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _isExiting = true;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _runtime.Dispose();
            _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _dispatcher.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Initialize()
    {
        var validation = SettingsValidator.Validate(
            _settings,
            _languageCatalog.GetLanguageOptions());
        if (validation is null)
        {
            TryActivateHooks();
        }
        else
        {
            _lastError = validation;
        }

        _server.Start();
        _refreshTimer.Start();
    }

    private Task<BrokerResponse> HandleRequestAsync(
        BrokerRequest request,
        CancellationToken cancellationToken) =>
        DispatchAsync(() => HandleRequest(request), cancellationToken);

    private BrokerResponse HandleRequest(BrokerRequest request)
    {
        try
        {
            return request.Command switch
            {
                BrokerCommand.GetStatus => Success(request),
                BrokerCommand.ActivateHooks => ActivateHooks(request),
                BrokerCommand.SaveSettings => SaveSettings(request),
                BrokerCommand.ConfigureStartup => ConfigureStartup(request),
                BrokerCommand.Stop => Stop(request),
                _ => Failure(request, "Unsupported broker command.")
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or Win32Exception or
            System.Runtime.InteropServices.COMException)
        {
            _lastError = exception.Message;
            AppLog.Write(
                $"Broker command {request.Command} failed with " +
                $"{exception.GetType().Name}: {exception.Message}");
            return Failure(request, exception.Message);
        }
    }

    private BrokerResponse ActivateHooks(BrokerRequest request)
    {
        var validation = SettingsValidator.Validate(
            _settings,
            _languageCatalog.GetLanguageOptions());
        if (validation is not null)
        {
            return Failure(request, validation);
        }

        return TryActivateHooks()
            ? Success(request)
            : Failure(request, "Another SmartLang process currently owns the input hooks.");
    }

    private BrokerResponse SaveSettings(BrokerRequest request)
    {
        if (request.Settings is null)
        {
            return Failure(request, "Settings were not supplied.");
        }

        var validation = SettingsValidator.Validate(
            request.Settings,
            _languageCatalog.GetLanguageOptions());
        if (validation is not null)
        {
            return Failure(request, validation);
        }

        _settingsStore.Save(request.Settings);
        _settings = request.Settings.Copy();
        _lastError = null;
        if (!_runtime.Restart(_settings))
        {
            _lastError = "Another SmartLang process currently owns the input hooks.";
        }

        return Success(request);
    }

    private BrokerResponse ConfigureStartup(BrokerRequest request)
    {
        var settings = request.Settings ?? _settings;
        _taskManager.Configure(
            settings.StartWithWindows,
            settings.AdministratorAppSupport);
        return Success(request);
    }

    private BrokerResponse Stop(BrokerRequest request)
    {
        _runtime.Stop();
        _refreshTimer.Stop();
        _ = Task.Delay(250).ContinueWith(
            _ => Dispatch(() =>
            {
                if (!_isExiting)
                {
                    _isExiting = true;
                    ExitThread();
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return Success(request);
    }

    private bool TryActivateHooks()
    {
        try
        {
            var active = _runtime.TryStart(_settings);
            if (active)
            {
                _lastError = null;
            }

            return active;
        }
        catch (Win32Exception exception)
        {
            _lastError = exception.Message;
            AppLog.Write($"Broker could not activate hooks: {exception.Message}");
            return false;
        }
    }

    private BrokerResponse Success(BrokerRequest request) =>
        new(
            BrokerProtocol.CurrentVersion,
            request.RequestId,
            true,
            GetStatus());

    private BrokerResponse Failure(BrokerRequest request, string error) =>
        new(
            BrokerProtocol.CurrentVersion,
            request.RequestId,
            false,
            GetStatus(),
            error);

    private BrokerStatus GetStatus() =>
        new(
            IsElevated: true,
            HooksActive: _runtime.IsRunning,
            Version: Application.ProductVersion,
            LastError: _lastError);

    private Task<T> DispatchAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        Dispatch(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private void Dispatch(Action action)
    {
        if (_isExiting || _dispatcher.IsDisposed)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException) when (_isExiting || _dispatcher.IsDisposed)
        {
        }
    }
}

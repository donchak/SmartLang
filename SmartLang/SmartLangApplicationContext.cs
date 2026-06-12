using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmartLang;

public sealed class SmartLangApplicationContext : ApplicationContext
{
    private readonly SingleInstanceCoordinator _singleInstance;
    private readonly SettingsStore _settingsStore = new();
    private readonly ScheduledTaskManager _taskManager = new();
    private readonly LanguageCatalog _languageCatalog = new();
    private readonly Control _dispatcher = new();
    private readonly Icon _applicationIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _healthTimer;
    private readonly System.Windows.Forms.Timer _hookRefreshTimer;
    private readonly HookRuntimeController _fallbackRuntime;
    private readonly BrokerClient _brokerClient;

    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private bool _isExiting;
    private bool _brokerCheckInProgress;
    private string _administratorStatus = "Administrator support has not been checked.";
    private bool _administratorStatusIsError;

    public SmartLangApplicationContext(SingleInstanceCoordinator singleInstance)
    {
        _singleInstance = singleInstance;
        _settings = _settingsStore.Load();
        var executablePath = Environment.ProcessPath;
        _applicationIcon = executablePath is not null
            ? Icon.ExtractAssociatedIcon(executablePath)
                ?? (Icon)SystemIcons.Application.Clone()
            : (Icon)SystemIcons.Application.Clone();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());

        _notifyIcon = new NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "SmartLang",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettings();

        _fallbackRuntime = new HookRuntimeController(
            Dispatch,
            error => Dispatch(() => ShowNotification(
                "Language switch failed",
                error,
                ToolTipIcon.Warning)));
        _brokerClient = new BrokerClient(
            Path.Combine(AppContext.BaseDirectory, "SmartLang.Broker.exe"),
            Application.ProductVersion);

        _healthTimer = new System.Windows.Forms.Timer { Interval = 3_000 };
        _healthTimer.Tick += async (_, _) => await EvaluateBrokerAsync(startIfMissing: false);
        _hookRefreshTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _hookRefreshTimer.Tick += (_, _) => _fallbackRuntime.Refresh();

        _dispatcher.CreateControl();
        _singleInstance.StartListening(() => Dispatch(OpenSettings));
        _dispatcher.BeginInvoke(async () => await InitializeAsync());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _healthTimer.Stop();
            _healthTimer.Dispose();
            _hookRefreshTimer.Stop();
            _hookRefreshTimer.Dispose();
            _fallbackRuntime.Dispose();
            _settingsForm?.Dispose();
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _applicationIcon.Dispose();
            _dispatcher.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task InitializeAsync()
    {
        AppLog.Write(
            $"Initializing tray. Primary={_settings.PrimaryLanguageTag}/{_settings.SecondaryLanguageTag}, " +
            $"shortcuts={_settings.PrimaryShortcut}/{_settings.AllLayoutsShortcut}, " +
            $"administratorSupport={_settings.AdministratorAppSupport}.");

        var validationMessage = GetValidationMessage();
        if (validationMessage is not null)
        {
            OpenSettings(validationMessage);
            SetAdministratorStatus("Waiting for valid language settings.", isError: true);
            return;
        }

        await EvaluateBrokerAsync(startIfMissing: true);
        _healthTimer.Start();
    }

    private void OpenSettings() => OpenSettings(GetValidationMessage());

    private void OpenSettings(string? validationMessage)
    {
        if (_isExiting)
        {
            return;
        }

        _settingsForm ??= CreateSettingsForm();
        var languages = _languageCatalog.GetLanguageOptions();
        _settingsForm.LoadSettings(_settings.Copy(), languages, validationMessage);

        if (!_settingsForm.Visible)
        {
            _settingsForm.Show();
        }

        if (_settingsForm.WindowState == FormWindowState.Minimized)
        {
            _settingsForm.WindowState = FormWindowState.Normal;
        }

        _settingsForm.BringToFront();
        _settingsForm.Activate();
    }

    private SettingsForm CreateSettingsForm()
    {
        var form = new SettingsForm(_applicationIcon);
        form.SetSaveHandler(SaveSettings);
        form.SetRestartAdministratorSupportHandler(
            async () => await RestartAdministratorSupportAsync());
        form.SetAdministratorSupportStatus(
            _administratorStatus,
            _administratorStatusIsError);
        return form;
    }

    private string? SaveSettings(AppSettings settings)
    {
        var languages = _languageCatalog.GetLanguageOptions();
        var validationMessage = SettingsValidator.Validate(settings, languages);
        if (validationMessage is not null)
        {
            return validationMessage;
        }

        try
        {
            _settingsStore.Save(settings);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return $"Could not save settings: {exception.Message}";
        }

        var previousAdministratorSupport = _settings.AdministratorAppSupport;
        _settings = settings.Copy();
        _ = ApplySettingsAsync(previousAdministratorSupport);
        return null;
    }

    private async Task ApplySettingsAsync(bool previousAdministratorSupport)
    {
        _healthTimer.Start();
        if (previousAdministratorSupport)
        {
            var saveResponse = await TrySendAsync(BrokerCommand.SaveSettings, _settings);
            if (saveResponse is not null)
            {
                await TrySendAsync(BrokerCommand.ConfigureStartup, _settings);
                if (!_settings.AdministratorAppSupport)
                {
                    await TrySendAsync(BrokerCommand.Stop);
                }
            }
        }

        if (_settings.AdministratorAppSupport)
        {
            await EvaluateBrokerAsync(startIfMissing: true);
        }
        else
        {
            TryConfigureTasksLocally();
            UseFallback("Administrator support is disabled.");
        }
    }

    private async Task EvaluateBrokerAsync(bool startIfMissing)
    {
        if (_isExiting || _brokerCheckInProgress)
        {
            return;
        }

        _brokerCheckInProgress = true;
        try
        {
            if (!_settings.AdministratorAppSupport)
            {
                UseFallback("Administrator support is disabled.");
                return;
            }

            if (!ProcessSecurity.IsProtectedInstallation(AppContext.BaseDirectory))
            {
                UseFallback(
                    "Administrator support requires installation under Program Files.");
                return;
            }

            var response = await TrySendAsync(BrokerCommand.GetStatus);
            if (response is null && startIfMissing)
            {
                if (!_taskManager.RunBroker())
                {
                    TryLaunchBrokerElevated();
                }

                for (var attempt = 0; attempt < 10 && response is null; attempt++)
                {
                    await Task.Delay(200);
                    response = await TrySendAsync(BrokerCommand.GetStatus);
                }
            }

            if (response is null)
            {
                UseFallback("Administrator support is unavailable; normal applications still work.");
                return;
            }

            if (!response.Success)
            {
                UseFallback(response.Error ?? "The administrator broker rejected the request.");
                return;
            }

            if (!response.Status.HooksActive)
            {
                StopFallback();
                response = await TrySendAsync(BrokerCommand.ActivateHooks);
            }

            if (response is { Success: true, Status.HooksActive: true })
            {
                StopFallback();
                if (startIfMissing)
                {
                    await TrySendAsync(BrokerCommand.ConfigureStartup, _settings);
                }

                SetAdministratorStatus("Administrator application support is active.", isError: false);
                return;
            }

            UseFallback(
                response?.Error ?? response?.Status.LastError ??
                "Administrator support could not acquire the input hooks.");
        }
        finally
        {
            _brokerCheckInProgress = false;
        }
    }

    private async Task RestartAdministratorSupportAsync()
    {
        if (!_settings.AdministratorAppSupport)
        {
            SetAdministratorStatus(
                "Enable administrator application support and save settings first.",
                isError: true);
            return;
        }

        SetAdministratorStatus("Restarting administrator application support...", isError: false);
        if (!_taskManager.RunBroker())
        {
            TryLaunchBrokerElevated();
        }

        await EvaluateBrokerAsync(startIfMissing: true);
    }

    private void TryLaunchBrokerElevated()
    {
        var brokerPath = Path.Combine(AppContext.BaseDirectory, "SmartLang.Broker.exe");
        if (!File.Exists(brokerPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = brokerPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            AppLog.Write($"Elevated broker launch was declined or failed: {exception.Message}");
        }
    }

    private async Task<BrokerResponse?> TrySendAsync(
        BrokerCommand command,
        AppSettings? settings = null)
    {
        try
        {
            return await _brokerClient.SendAsync(command, settings);
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException or OperationCanceledException or
            InvalidDataException or UnauthorizedAccessException or Win32Exception)
        {
            AppLog.Write($"Broker command {command} is unavailable: {exception.Message}");
            return null;
        }
    }

    private void UseFallback(string status)
    {
        try
        {
            if (!_fallbackRuntime.IsRunning && _fallbackRuntime.TryStart(_settings))
            {
                _hookRefreshTimer.Start();
            }

            SetAdministratorStatus(status, isError: true);
        }
        catch (Win32Exception exception)
        {
            SetAdministratorStatus(
                $"No input hooks are active: {exception.Message}",
                isError: true);
        }
    }

    private void StopFallback()
    {
        _hookRefreshTimer.Stop();
        _fallbackRuntime.Stop();
    }

    private void TryConfigureTasksLocally()
    {
        try
        {
            _taskManager.Configure(
                _settings.StartWithWindows,
                _settings.AdministratorAppSupport);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            AppLog.Write($"Could not configure startup tasks locally: {exception.Message}");
        }
    }

    private string? GetValidationMessage() =>
        SettingsValidator.Validate(_settings, _languageCatalog.GetLanguageOptions());

    private void SetAdministratorStatus(string status, bool isError)
    {
        _administratorStatus = status;
        _administratorStatusIsError = isError;
        _notifyIcon.Text = isError
            ? "SmartLang - limited administrator support"
            : "SmartLang - administrator support active";
        _settingsForm?.SetAdministratorSupportStatus(status, isError);
    }

    private void ShowNotification(string title, string text, ToolTipIcon icon)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(4000);
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

    private async Task ExitAsync()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _healthTimer.Stop();
        StopFallback();
        if (_settings.AdministratorAppSupport)
        {
            await TrySendAsync(BrokerCommand.Stop);
        }

        _settingsForm?.AllowClose();
        _settingsForm?.Close();
        _notifyIcon.Visible = false;
        ExitThread();
    }
}

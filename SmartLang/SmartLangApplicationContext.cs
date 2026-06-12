using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmartLang;

public sealed class SmartLangApplicationContext: ApplicationContext {
    readonly SingleInstanceCoordinator singleInstance;
    readonly SettingsStore settingsStore = new();
    readonly ScheduledTaskManager taskManager = new();
    readonly LanguageCatalog languageCatalog = new();
    readonly Control dispatcher = new();
    readonly Icon applicationIcon;
    readonly NotifyIcon notifyIcon;
    readonly System.Windows.Forms.Timer healthTimer;
    readonly System.Windows.Forms.Timer hookRefreshTimer;
    readonly HookRuntimeController fallbackRuntime;
    readonly BrokerClient brokerClient;

    AppSettings settings;
    SettingsForm? settingsForm;
    bool isExiting;
    bool brokerCheckInProgress;
    string administratorStatus = "Administrator support has not been checked.";
    bool administratorStatusIsError;

    public SmartLangApplicationContext(SingleInstanceCoordinator singleInstance) {
        this.singleInstance = singleInstance;
        settings = settingsStore.Load();
        var executablePath = Environment.ProcessPath;
        applicationIcon = executablePath is not null
            ? Icon.ExtractAssociatedIcon(executablePath)
                ?? (Icon)SystemIcons.Application.Clone()
            : (Icon)SystemIcons.Application.Clone();

        var applicationVersion = Application.ProductVersion;
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"SmartLang v{applicationVersion}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());

        notifyIcon = new NotifyIcon {
            Icon = applicationIcon,
            Text = "SmartLang",
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => OpenSettings();

        fallbackRuntime = new HookRuntimeController(
            Dispatch,
            error => Dispatch(() => ShowNotification("Language switch failed", error, ToolTipIcon.Warning)));
        brokerClient = new BrokerClient(
            Path.Combine(AppContext.BaseDirectory, "SmartLang.Broker.exe"),
            Application.ProductVersion);

        healthTimer = new System.Windows.Forms.Timer { Interval = 3_000 };
        healthTimer.Tick += async (_, _) => await EvaluateBrokerAsync(startIfMissing: false);
        hookRefreshTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        hookRefreshTimer.Tick += (_, _) => fallbackRuntime.Refresh();

        dispatcher.CreateControl();
        this.singleInstance.StartListening(() => Dispatch(OpenSettings));
        dispatcher.BeginInvoke(async () => await InitializeAsync());
    }

    protected override void Dispose(bool disposing) {
        if(disposing) {
            healthTimer.Stop();
            healthTimer.Dispose();
            hookRefreshTimer.Stop();
            hookRefreshTimer.Dispose();
            fallbackRuntime.Dispose();
            settingsForm?.Dispose();
            notifyIcon.ContextMenuStrip?.Dispose();
            notifyIcon.Dispose();
            applicationIcon.Dispose();
            dispatcher.Dispose();
        }

        base.Dispose(disposing);
    }

    async Task InitializeAsync() {
        AppLog.Write(
            $"Initializing tray. Primary={settings.PrimaryLanguageTag}/{settings.SecondaryLanguageTag}, " +
            $"shortcuts={settings.PrimaryShortcut}/{settings.AllLayoutsShortcut}, " +
            $"administratorSupport={settings.AdministratorAppSupport}.");

        var validationMessage = GetValidationMessage();
        if(validationMessage is not null) {
            OpenSettings(validationMessage);
            SetAdministratorStatus("Waiting for valid language settings.", isError: true);
            return;
        }

        await EvaluateBrokerAsync(startIfMissing: true);
        healthTimer.Start();
    }

    void OpenSettings() => OpenSettings(GetValidationMessage());

    void OpenSettings(string? validationMessage) {
        if(isExiting) {
            return;
        }

        settingsForm ??= CreateSettingsForm();
        var languages = languageCatalog.GetLanguageOptions();
        settingsForm.LoadSettings(settings.Copy(), languages, validationMessage);

        if(!settingsForm.Visible) {
            settingsForm.Show();
        }

        if(settingsForm.WindowState == FormWindowState.Minimized) {
            settingsForm.WindowState = FormWindowState.Normal;
        }

        settingsForm.BringToFront();
        settingsForm.Activate();
    }

    SettingsForm CreateSettingsForm() {
        var form = new SettingsForm(applicationIcon, Application.ProductVersion);
        form.SetSaveHandler(SaveSettings);
        form.SetRestartAdministratorSupportHandler(async () => await RestartAdministratorSupportAsync());
        form.SetAdministratorSupportStatus(administratorStatus, administratorStatusIsError);
        return form;
    }

    string? SaveSettings(AppSettings settings) {
        var languages = languageCatalog.GetLanguageOptions();
        var validationMessage = SettingsValidator.Validate(settings, languages);
        if(validationMessage is not null) {
            return validationMessage;
        }

        try {
            settingsStore.Save(settings);
        } catch(Exception exception) when(
              exception is UnauthorizedAccessException or IOException or InvalidOperationException) {
            return $"Could not save settings: {exception.Message}";
        }

        var previousAdministratorSupport = this.settings.AdministratorAppSupport;
        this.settings = settings.Copy();
        _ = ApplySettingsAsync(previousAdministratorSupport);
        return null;
    }

    async Task ApplySettingsAsync(bool previousAdministratorSupport) {
        healthTimer.Start();
        if(previousAdministratorSupport) {
            var saveResponse = await TrySendAsync(BrokerCommand.SaveSettings, settings);
            if(saveResponse is not null) {
                await TrySendAsync(BrokerCommand.ConfigureStartup, settings);
                if(!settings.AdministratorAppSupport) {
                    await TrySendAsync(BrokerCommand.Stop);
                }
            }
        }

        if(settings.AdministratorAppSupport) {
            await EvaluateBrokerAsync(startIfMissing: true);
        } else {
            TryConfigureTasksLocally();
            UseFallback("Administrator support is disabled.");
        }
    }

    async Task EvaluateBrokerAsync(bool startIfMissing) {
        if(isExiting || brokerCheckInProgress) {
            return;
        }

        brokerCheckInProgress = true;
        try {
            if(!settings.AdministratorAppSupport) {
                UseFallback("Administrator support is disabled.");
                return;
            }

            if(!ProcessSecurity.IsProtectedInstallation(AppContext.BaseDirectory)) {
                UseFallback("Administrator support requires installation under Program Files.");
                return;
            }

            var response = await TrySendAsync(BrokerCommand.GetStatus);
            if(response is null && startIfMissing) {
                if(!taskManager.RunBroker()) {
                    TryLaunchBrokerElevated();
                }

                for(var attempt = 0; attempt < 10 && response is null; attempt++) {
                    await Task.Delay(200);
                    response = await TrySendAsync(BrokerCommand.GetStatus);
                }
            }

            if(response is null) {
                UseFallback("Administrator support is unavailable; normal applications still work.");
                return;
            }

            if(!response.Success) {
                UseFallback(response.Error ?? "The administrator broker rejected the request.");
                return;
            }

            if(!response.Status.HooksActive) {
                StopFallback();
                response = await TrySendAsync(BrokerCommand.ActivateHooks);
            }

            if(response is { Success: true, Status.HooksActive: true }) {
                StopFallback();
                if(startIfMissing) {
                    await TrySendAsync(BrokerCommand.ConfigureStartup, settings);
                }

                SetAdministratorStatus("Administrator application support is active.", isError: false);
                return;
            }

            UseFallback(
                response?.Error ??
                response?.Status.LastError ??
                "Administrator support could not acquire the input hooks.");
        } finally {
            brokerCheckInProgress = false;
        }
    }

    async Task RestartAdministratorSupportAsync() {
        if(!settings.AdministratorAppSupport) {
            SetAdministratorStatus("Enable administrator application support and save settings first.", isError: true);
            return;
        }

        SetAdministratorStatus("Restarting administrator application support...", isError: false);
        if(!taskManager.RunBroker()) {
            TryLaunchBrokerElevated();
        }

        await EvaluateBrokerAsync(startIfMissing: true);
    }

    void TryLaunchBrokerElevated() {
        var brokerPath = Path.Combine(AppContext.BaseDirectory, "SmartLang.Broker.exe");
        if(!File.Exists(brokerPath)) {
            return;
        }

        try {
            Process.Start(new ProcessStartInfo {
                FileName = brokerPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
        } catch(Exception exception) when(
              exception is Win32Exception or InvalidOperationException) {
            AppLog.Write($"Elevated broker launch was declined or failed: {exception.Message}");
        }
    }

    async Task<BrokerResponse?> TrySendAsync(
        BrokerCommand command,
        AppSettings? settings = null) {
        try {
            return await brokerClient.SendAsync(command, settings);
        } catch(Exception exception) when(
              exception is IOException or TimeoutException or OperationCanceledException or
              InvalidDataException or UnauthorizedAccessException or Win32Exception) {
            AppLog.Write($"Broker command {command} is unavailable: {exception.Message}");
            return null;
        }
    }

    void UseFallback(string status) {
        try {
            if(!fallbackRuntime.IsRunning && fallbackRuntime.TryStart(settings)) {
                hookRefreshTimer.Start();
            }

            SetAdministratorStatus(status, isError: true);
        } catch(Win32Exception exception) {
            SetAdministratorStatus($"No input hooks are active: {exception.Message}", isError: true);
        }
    }

    void StopFallback() {
        hookRefreshTimer.Stop();
        fallbackRuntime.Stop();
    }

    void TryConfigureTasksLocally() {
        try {
            taskManager.Configure(settings.StartWithWindows, settings.AdministratorAppSupport);
        } catch(Exception exception) when(
              exception is InvalidOperationException or UnauthorizedAccessException or COMException) {
            AppLog.Write($"Could not configure startup tasks locally: {exception.Message}");
        }
    }

    string? GetValidationMessage() =>
        SettingsValidator.Validate(settings, languageCatalog.GetLanguageOptions());

    void SetAdministratorStatus(string status, bool isError) {
        administratorStatus = status;
        administratorStatusIsError = isError;
        notifyIcon.Text = isError ? "SmartLang - limited administrator support" : "SmartLang - administrator support active";
        settingsForm?.SetAdministratorSupportStatus(status, isError);
    }

    void ShowNotification(string title, string text, ToolTipIcon icon) {
        notifyIcon.BalloonTipTitle = title;
        notifyIcon.BalloonTipText = text;
        notifyIcon.BalloonTipIcon = icon;
        notifyIcon.ShowBalloonTip(4000);
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

    async Task ExitAsync() {
        if(isExiting) {
            return;
        }

        isExiting = true;
        healthTimer.Stop();
        StopFallback();
        if(settings.AdministratorAppSupport) {
            await TrySendAsync(BrokerCommand.Stop);
        }

        settingsForm?.AllowClose();
        settingsForm?.Close();
        notifyIcon.Visible = false;
        ExitThread();
    }
}

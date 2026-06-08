using System.ComponentModel;

namespace SmartLang;

public sealed class SmartLangApplicationContext : ApplicationContext
{
    private readonly SingleInstanceCoordinator _singleInstance;
    private readonly SettingsStore _settingsStore = new();
    private readonly StartupManager _startupManager = new();
    private readonly LanguageCatalog _languageCatalog = new();
    private readonly Control _dispatcher = new();
    private readonly Icon _applicationIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _hookRefreshTimer;

    private AppSettings _settings;
    private KeyboardLayoutService? _keyboardLayoutService;
    private KeyboardHook? _keyboardHook;
    private SettingsForm? _settingsForm;
    private bool _isExiting;

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
        menu.Items.Add("Exit", null, (_, _) => Exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "SmartLang",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettings();

        _hookRefreshTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _hookRefreshTimer.Tick += (_, _) => _keyboardHook?.Refresh();

        _dispatcher.CreateControl();
        _singleInstance.StartListening(() => Dispatch(OpenSettings));
        _dispatcher.BeginInvoke(Initialize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hookRefreshTimer.Stop();
            _hookRefreshTimer.Dispose();
            _keyboardHook?.Dispose();
            _keyboardLayoutService?.Dispose();
            _settingsForm?.Dispose();
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _applicationIcon.Dispose();
            _dispatcher.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Initialize()
    {
        AppLog.Write(
            $"Initializing. Primary={_settings.PrimaryLanguageTag}/{_settings.SecondaryLanguageTag}, " +
            $"shortcuts={_settings.PrimaryShortcut}/{_settings.AllLayoutsShortcut}.");

        try
        {
            _startupManager.Apply(_settings.StartWithWindows);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            ShowNotification("Startup setting", exception.Message, ToolTipIcon.Warning);
        }

        try
        {
            _keyboardLayoutService = new KeyboardLayoutService(_languageCatalog);
        }
        catch (Win32Exception exception)
        {
            AppLog.Write($"Could not initialize layout activator: {exception.Message}");
            ShowNotification(
                "Layout activator unavailable",
                exception.Message,
                ToolTipIcon.Error);
            OpenSettings(exception.Message);
            return;
        }

        var validationMessage = GetValidationMessage();
        if (validationMessage is null)
        {
            EnableKeyboardHook();
        }
        else
        {
            OpenSettings(validationMessage);
        }
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
            _startupManager.Apply(settings.StartWithWindows);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return $"Could not save settings: {exception.Message}";
        }

        _settings = settings.Copy();
        DisableKeyboardHook();
        EnableKeyboardHook();
        return null;
    }

    private void EnableKeyboardHook()
    {
        if (_keyboardHook is not null)
        {
            return;
        }

        try
        {
            _keyboardHook = new KeyboardHook(
                GetEnabledShortcuts(),
                shortcut => Dispatch(() => HandleShortcut(shortcut)));
            _keyboardHook.Start();
            _hookRefreshTimer.Start();
        }
        catch (Exception exception)
        {
            _hookRefreshTimer.Stop();
            _keyboardHook?.Dispose();
            _keyboardHook = null;
            ShowNotification("Keyboard hook unavailable", exception.Message, ToolTipIcon.Error);
            OpenSettings(exception.Message);
        }
    }

    private void DisableKeyboardHook()
    {
        _hookRefreshTimer.Stop();
        _keyboardHook?.Dispose();
        _keyboardHook = null;
    }

    private void HandleShortcut(ShortcutKind shortcut)
    {
        try
        {
            HandleShortcutCore(shortcut);
        }
        catch (Exception exception)
        {
            AppLog.Write(
                $"Shortcut {shortcut} failed with {exception.GetType().Name}: {exception.Message}");
            ShowNotification(
                "Language switch failed",
                $"Unexpected error. Details were written to {AppLog.FilePath}.",
                ToolTipIcon.Error);
        }
    }

    private void HandleShortcutCore(ShortcutKind shortcut)
    {
        var validationMessage = GetValidationMessage();
        if (validationMessage is not null)
        {
            DisableKeyboardHook();
            ShowNotification("Settings need attention", validationMessage, ToolTipIcon.Warning);
            OpenSettings(validationMessage);
            return;
        }

        if (_keyboardLayoutService is null)
        {
            DisableKeyboardHook();
            return;
        }

        bool switched;
        if (shortcut == _settings.PrimaryShortcut)
        {
            switched = _keyboardLayoutService.TogglePrimaryLanguages(_settings);
        }
        else if (_settings.AllLayoutsShortcut != ShortcutKind.None &&
            shortcut == _settings.AllLayoutsShortcut)
        {
            switched = _keyboardLayoutService.CycleAllLayouts();
        }
        else
        {
            return;
        }

        if (!switched)
        {
            AppLog.Write($"Shortcut {shortcut} did not change the active layout.");
            ShowNotification(
                "Language switch failed",
                $"Windows did not accept the layout change. Details were written to {AppLog.FilePath}.",
                ToolTipIcon.Warning);
        }
        else
        {
            AppLog.Write($"Shortcut {shortcut} completed.");
        }
    }

    private string? GetValidationMessage() =>
        SettingsValidator.Validate(_settings, _languageCatalog.GetLanguageOptions());

    private IReadOnlyCollection<ShortcutKind> GetEnabledShortcuts()
    {
        var shortcuts = new HashSet<ShortcutKind> { _settings.PrimaryShortcut };
        if (_settings.AllLayoutsShortcut != ShortcutKind.None)
        {
            shortcuts.Add(_settings.AllLayoutsShortcut);
        }

        return shortcuts;
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

    private void Exit()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        DisableKeyboardHook();
        _settingsForm?.AllowClose();
        _settingsForm?.Close();
        _notifyIcon.Visible = false;
        ExitThread();
    }
}

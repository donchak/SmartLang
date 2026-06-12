using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SmartLang;

public sealed class HookRuntimeController : IDisposable
{
    private readonly Action<Action> _dispatch;
    private readonly Action<string> _failure;
    private Mutex? _ownershipMutex;
    private KeyboardLayoutService? _layoutService;
    private KeyboardHook? _keyboardHook;
    private AppSettings? _settings;

    public HookRuntimeController(Action<Action> dispatch, Action<string> failure)
    {
        _dispatch = dispatch;
        _failure = failure;
    }

    public bool IsRunning => _keyboardHook is not null;

    public bool TryStart(AppSettings settings)
    {
        if (IsRunning)
        {
            return true;
        }

        var ownershipMutex = new Mutex(false, BuildMutexName());
        var acquired = false;
        try
        {
            try
            {
                acquired = ownershipMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                ownershipMutex.Dispose();
                return false;
            }

            _settings = settings.Copy();
            _layoutService = new KeyboardLayoutService(new LanguageCatalog());
            _keyboardHook = new KeyboardHook(
                GetEnabledShortcuts(_settings),
                shortcut => _dispatch(() => HandleShortcut(shortcut)));
            _keyboardHook.Start();
            _ownershipMutex = ownershipMutex;
            AppLog.Write("Hook runtime acquired input ownership.");
            return true;
        }
        catch
        {
            _keyboardHook?.Dispose();
            _keyboardHook = null;
            _layoutService?.Dispose();
            _layoutService = null;
            _settings = null;
            if (acquired)
            {
                ownershipMutex.ReleaseMutex();
            }

            ownershipMutex.Dispose();
            throw;
        }
    }

    public bool Restart(AppSettings settings)
    {
        Stop();
        return TryStart(settings);
    }

    public void Refresh() => _keyboardHook?.Refresh();

    public void Stop()
    {
        _keyboardHook?.Dispose();
        _keyboardHook = null;
        _layoutService?.Dispose();
        _layoutService = null;
        _settings = null;

        if (_ownershipMutex is not null)
        {
            try
            {
                _ownershipMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _ownershipMutex.Dispose();
            _ownershipMutex = null;
            AppLog.Write("Hook runtime released input ownership.");
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void HandleShortcut(ShortcutKind shortcut)
    {
        if (_settings is null || _layoutService is null)
        {
            return;
        }

        try
        {
            var switched = shortcut == _settings.PrimaryShortcut
                ? _layoutService.TogglePrimaryLanguages(_settings)
                : _settings.AllLayoutsShortcut != ShortcutKind.None &&
                  shortcut == _settings.AllLayoutsShortcut &&
                  _layoutService.CycleAllLayouts();

            if (!switched)
            {
                _failure($"Windows did not accept the {shortcut} layout change.");
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(
                $"Shortcut {shortcut} failed with {exception.GetType().Name}: {exception.Message}");
            _failure($"Unexpected language-switch error: {exception.Message}");
        }
    }

    private static IReadOnlyCollection<ShortcutKind> GetEnabledShortcuts(AppSettings settings)
    {
        var shortcuts = new HashSet<ShortcutKind> { settings.PrimaryShortcut };
        if (settings.AllLayoutsShortcut != ShortcutKind.None)
        {
            shortcuts.Add(settings.AllLayoutsShortcut);
        }

        return shortcuts;
    }

    private static string BuildMutexName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var sessionId = Environment.ProcessId;
        try
        {
            sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        }
        catch (InvalidOperationException)
        {
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
        return $@"Local\SmartLang.HookOwner.{sessionId}.{hash}";
    }
}

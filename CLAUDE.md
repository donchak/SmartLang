# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

SmartLang is a Windows tray application (WinForms, .NET 10, `net10.0-windows`, win-x64) that toggles between two primary input languages with one shortcut while still supporting cycling through every installed keyboard layout. It runs without elevation.

## Build, test, publish

Use the solution file `SmartLang.slnx` (NOT a `.sln`). All commands run from the repo root.

```powershell
dotnet restore SmartLang.slnx
dotnet build SmartLang.slnx --no-restore
dotnet test SmartLang.slnx --no-build --no-restore
```

Run a single test (xUnit, via `Microsoft.NET.Test.Sdk`):

```powershell
dotnet test SmartLang.slnx --filter "FullyQualifiedName~KeyboardShortcutEngineTests.CtrlShiftTriggersOnceWhenModifierIsReleased"
```

Produce the self-contained, single-file executable at `artifacts\publish\SmartLang.exe`:

```powershell
dotnet publish SmartLang\SmartLang.csproj -c Release -o artifacts\publish
```

The app project sets `PublishSingleFile=true`, `SelfContained=true`, `PublishTrimmed=false`. Do not enable trimming — WinForms / reflection-heavy code paths are not trim-safe here. Do not add a manifest that requests admin; the app is designed to run non-elevated.

## Architecture

Composition root is `SmartLangApplicationContext` (constructed from `Program.Main` after `SingleInstanceCoordinator` confirms this is the first instance). It owns every long-lived service and is the only place that wires them together:

- `SingleInstanceCoordinator` — enforces single instance and signals the existing instance to open Settings.
- `SettingsStore` — JSON persistence at `%LocalAppData%\SmartLang\settings.json` (schema versioned via `AppSettings.CurrentVersion`).
- `SettingsValidator` — pure validation; always run results through it before enabling the hook or saving.
- `StartupManager` — toggles the per-user `Run` registry key for start-at-sign-in.
- `LanguageCatalog` — enumerates installed Windows keyboard layouts / languages.
- `KeyboardLayoutService` — selects the target layout and delegates activation to `NativeInputProfileActivator`. It remembers the last-used layout per language tag so toggling restores the user's preferred variant.
- `SmartLang.NativeHook` — a small x64 native `WH_GETMESSAGE` hook. SmartLang posts a private thread message to the foreground UI thread; the hook consumes it before the application sees it and calls `ActivateKeyboardLayout` in that thread. Do not switch layouts by posting `WM_INPUTLANGCHANGEREQUEST`, using TSF from the SmartLang thread, or emulating `Win+Space`; those approaches either affect the wrong thread or hang/misbehave in applications such as Visual Studio.
- `KeyboardHook` + `KeyboardShortcutEngine` — `KeyboardHook` installs a `WH_KEYBOARD_LL` low-level hook; `KeyboardShortcutEngine` is the pure state machine that decides when `Ctrl+Shift` / `Win+Space` should fire and whether to suppress the key. **All shortcut logic belongs in `KeyboardShortcutEngine` so it stays unit-testable** — the hook should remain a thin Win32 shim.
- `SettingsForm` — the only UI. Closing it hides it; the tray menu's `Exit` is the only way to terminate.

### Threading

The hook callback runs on the hook thread. `SmartLangApplicationContext._dispatcher` is a hidden `Control` used to marshal back to the UI thread via `BeginInvoke` (see the `Dispatch` method). Any callback originating from `KeyboardHook` or `SingleInstanceCoordinator` must be marshalled through `Dispatch` before touching UI or shared state.

### Key invariants

- `ShortcutKind.None` is a valid value for `AppSettings.AllLayoutsShortcut` and means "disabled" — only the primary-language shortcut is active. `PrimaryShortcut` must always be a real shortcut.
- P/Invoke signatures live exclusively in `NativeMethods`. Add new Win32 calls there rather than scattering `DllImport`s.
- `SmartLang.csproj` exposes internals to the test project via `<InternalsVisibleTo Include="SmartLang.Tests" />`, so tests reach `internal` members directly — no need to widen visibility.

## Conventions

- File-scoped namespaces, `Nullable` enabled, `ImplicitUsings` enabled (don't add redundant `using System;` etc.).
- Catch only the specific exceptions the surrounding code already filters (`UnauthorizedAccessException`, `IOException`, `InvalidOperationException`, `Win32Exception`); surface failures to the user via `NotifyIcon` balloon tips, not dialogs.
- Settings are passed around as immutable snapshots — call `AppSettings.Copy()` when handing one across a boundary; never mutate the live `_settings` instance directly.
- Tests are xUnit (`[Fact]` / `[Theory]` + `[InlineData]`) in `SmartLang.Tests`, one test class per production class, named `<ClassName>Tests`.

# SmartLang

SmartLang is a Windows tray application that keeps two input languages one
shortcut away while retaining access to every installed keyboard layout.

## Requirements

- Windows 10 or Windows 11, x64
- .NET 10 SDK for development
- WiX Toolset 4 packages are restored automatically when building the MSI
- Visual Studio or Visual Studio Build Tools with:
  - Desktop development with C++
  - MSVC x86/x64 build tools
  - Windows 10 or Windows 11 SDK

## Build Everything

From a PowerShell terminal in the repository root:

```powershell
.\build.ps1
```

This restores packages, builds the tray, elevated broker, native hooks and x86
host, runs all tests, publishes to `artifacts\publish`, and creates a versioned
installer such as `artifacts\installer\SmartLang.v0.8.0.msi`.

## Release Version

The checked-in application version is controlled in `Version.props`:

```xml
<SmartLangVersion>1.0.0</SmartLangVersion>
```

Use numeric `major.minor.patch` format. This single value controls the assembly,
file, informational, broker protocol status, and MSI product versions. Git
commit hashes are not appended to the displayed application version.

For a one-off release build without editing `Version.props`, pass:

```powershell
.\build.ps1 -Version 1.2.3
```

The MSI supports major upgrades, so installing a higher version replaces the
older SmartLang installation. Windows Installer limits major and minor values
to 255 and the patch value to 65535.

Deploy the entire publish folder. These runtime files must remain together:

- `SmartLang.exe`
- `SmartLang.Broker.exe`
- `SmartLang.NativeHook.dll` for 64-bit applications
- `SmartLang.NativeHook32.dll` for 32-bit applications
- `SmartLang.NativeHost32.exe`

The MSI installs these files under `%ProgramFiles%\SmartLang`. The elevated
broker loads hook shadow copies from protected
`%ProgramData%\SmartLang\NativeHooks`; portable fallback mode uses
`%LocalAppData%\SmartLang\NativeHooks`. Stale copies are removed automatically.

Optional arguments:

```powershell
.\build.ps1 -Configuration Debug
.\build.ps1 -SkipTests
.\build.ps1 -SkipInstaller
.\build.ps1 -OutputDirectory C:\Builds\SmartLang
.\build.ps1 -Version 1.2.3
```

For signed releases, provide `-SigningCertificateThumbprint` and optionally
`-TimestampUrl`. The build signs both executables, native files, and the MSI.

## Individual Commands

```powershell
dotnet restore SmartLang.slnx
dotnet build SmartLang.slnx --no-restore
dotnet test SmartLang.slnx --no-build --no-restore
```

Publish the self-contained tray and broker:

```powershell
dotnet publish SmartLang\SmartLang.csproj -c Release -o artifacts\publish
dotnet publish SmartLang.Broker\SmartLang.Broker.csproj -c Release -o artifacts\publish
```

Building or publishing either application builds both native hook
architectures and the x86 host through MSBuild.

The application icon source is
`SmartLang\Assets\smartlang-icon.png`; the embedded Windows icon is
`SmartLang\Assets\SmartLang.ico`.

## Behavior

- First launch opens Settings; later launches start in the tray.
- `Ctrl+Shift` and `Win+Space` can be assigned to either primary-language
  toggling or cycling all installed layouts.
- Cycling all layouts can be set to `None`, leaving only the primary-language
  shortcut active.
- Closing Settings hides it. Use the tray menu's `Exit` command to stop the app.
- Settings are stored in `%LocalAppData%\SmartLang\settings.json`.
- Administrator support is enabled by default for MSI installations.
- A normal-integrity tray task and highest-privilege broker task start at
  sign-in. Both tasks run only in the installing user's interactive session.
- The broker owns all input hooks while healthy. If it is unavailable, the tray
  acquires the same ownership lease and supports normal applications.
- Tray **Exit** stops both processes for the current session without removing
  sign-in configuration.
- Settings include a broker health indicator and restart action.

SmartLang uses a small native hook helper to activate the layout inside the
foreground application's input thread. The tray remains unelevated; only the
minimal broker and its hook hosts run elevated.

Automatic administrator support is intentionally disabled for portable copies.
Install the MSI under `Program Files` so a normal process cannot replace a
binary that Task Scheduler later starts elevated.

Windows does not load ordinary global hook DLLs into packaged WinRT
applications. For their `Windows.UI.Core.CoreWindow` input windows, SmartLang
posts `WM_INPUTLANGCHANGEREQUEST` directly and verifies the resulting HKL.

Secure desktop and protected processes are outside SmartLang's supported
surface.

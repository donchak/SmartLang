# SmartLang

SmartLang is a Windows tray application that keeps two input languages one
shortcut away while retaining access to every installed keyboard layout.

## Requirements

- Windows 10 or Windows 11, x64
- .NET 10 SDK for development
- Visual Studio or Visual Studio Build Tools with:
  - Desktop development with C++
  - MSVC x86/x64 build tools
  - Windows 10 or Windows 11 SDK

## Build Everything

From a PowerShell terminal in the repository root:

```powershell
.\build.ps1
```

This restores packages, builds the C# application and both native hook DLLs,
runs all tests, and publishes the complete application to
`artifacts\publish`.

Deploy the entire publish folder. These runtime files must remain together:

- `SmartLang.exe`
- `SmartLang.NativeHook.dll` for 64-bit applications
- `SmartLang.NativeHook32.dll` for 32-bit applications

At runtime SmartLang loads per-process shadow copies of the native hook DLLs
from `%LocalAppData%\SmartLang\NativeHooks`. This keeps the published DLLs
replaceable while Windows still has injected hook modules loaded in other
processes. Stale shadow copies are removed automatically when Windows releases
them.

Optional arguments:

```powershell
.\build.ps1 -Configuration Debug
.\build.ps1 -SkipTests
.\build.ps1 -OutputDirectory C:\Builds\SmartLang
```

## Individual Commands

```powershell
dotnet restore SmartLang.slnx
dotnet build SmartLang.slnx --no-restore
dotnet test SmartLang.slnx --no-build --no-restore
```

Create the portable, self-contained executable:

```powershell
dotnet publish SmartLang\SmartLang.csproj -c Release -o artifacts\publish
```

Building or publishing `SmartLang.csproj` automatically builds both native
hook architectures through MSBuild.

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
- Start-at-sign-in uses the current user's Windows `Run` registry key.

SmartLang uses a small native hook helper to activate the layout inside the
foreground application's input thread. It does not send layout-change window
messages or emulate the Windows layout shortcut.
Running it as administrator is neither required nor recommended.

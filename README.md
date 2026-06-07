# SmartLang

SmartLang is a Windows tray application that keeps two input languages one
shortcut away while retaining access to every installed keyboard layout.

## Requirements

- Windows 10 or Windows 11, x64
- .NET 10 SDK for development

## Build and test

```powershell
dotnet restore SmartLang.slnx
dotnet build SmartLang.slnx --no-restore
dotnet test SmartLang.slnx --no-build --no-restore
```

Create the portable, self-contained executable:

```powershell
dotnet publish SmartLang\SmartLang.csproj -c Release -o artifacts\publish
```

The published application is `artifacts\publish\SmartLang.exe`.

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

SmartLang runs without elevation. Windows may reject layout-change messages
sent to an elevated application or a secure desktop.

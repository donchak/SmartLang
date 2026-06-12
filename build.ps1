[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutputDirectory = 'artifacts\publish',

    [switch] $SkipTests,

    [switch] $SkipInstaller,

    [string] $Version,

    [string] $SigningCertificateThumbprint,

    [string] $TimestampUrl
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = $PSScriptRoot
$solution = Join-Path $repositoryRoot 'SmartLang.slnx'
$project = Join-Path $repositoryRoot 'SmartLang\SmartLang.csproj'
$brokerProject = Join-Path $repositoryRoot 'SmartLang.Broker\SmartLang.Broker.csproj'
$installerProject = Join-Path $repositoryRoot 'SmartLang.Installer\SmartLang.Installer.wixproj'
$versionFile = Join-Path $repositoryRoot 'Version.props'
$publishDirectory = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $OutputDirectory))
$installerDirectory = Join-Path $repositoryRoot 'artifacts\installer'

[xml] $versionDocument = Get-Content -LiteralPath $versionFile
$configuredVersion = [string] $versionDocument.Project.PropertyGroup.SmartLangVersion
$effectiveVersion = if ([string]::IsNullOrWhiteSpace($Version)) {
    $configuredVersion
}
else {
    $Version
}

if ($effectiveVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$effectiveVersion' must use numeric major.minor.patch format, for example 1.2.3."
}

$versionParts = $effectiveVersion.Split('.') | ForEach-Object { [int] $_ }
if ($versionParts[0] -gt 255 -or $versionParts[1] -gt 255 -or $versionParts[2] -gt 65535) {
    throw "Version '$effectiveVersion' exceeds Windows Installer limits: major/minor <= 255 and patch <= 65535."
}

$installerFileName = "SmartLang.v$effectiveVersion.msi"
$installerPath = Join-Path $installerDirectory $installerFileName

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)

    & dotnet @Arguments "-p:SmartLangVersion=$effectiveVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Signing {
    param([Parameter(Mandatory = $true)][string[]] $Paths)

    if ([string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
        return
    }

    $signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -eq $signTool) {
        throw 'signtool.exe was not found. Install a Windows SDK or add signtool.exe to PATH.'
    }

    foreach ($path in $Paths) {
        $arguments = @(
            'sign',
            '/sha1', $SigningCertificateThumbprint,
            '/fd', 'SHA256'
        )
        if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
            $arguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
        }

        $arguments += $path
        & $signTool.Source @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Signing '$path' failed with exit code $LASTEXITCODE."
        }
    }
}

if ($env:OS -ne 'Windows_NT') {
    throw 'SmartLang can only be built on Windows.'
}

Push-Location $repositoryRoot
try {
    Write-Host "SmartLang version: $effectiveVersion"
    Write-Host 'Restoring packages...'
    Invoke-DotNet restore $solution '-m:1' --disable-parallel

    Write-Host "Building SmartLang and native x64/x86 hooks ($Configuration)..."
    Invoke-DotNet build $solution `
        --configuration $Configuration `
        --no-restore `
        '-m:1' `
        --verbosity minimal

    if (-not $SkipTests) {
        Write-Host 'Running tests...'
        Invoke-DotNet test $solution `
            --configuration $Configuration `
            --no-build `
            --no-restore `
            '-m:1' `
            --verbosity minimal
    }

    Write-Host "Publishing to $publishDirectory..."
    Invoke-DotNet publish $project `
        --configuration $Configuration `
        --no-restore `
        --output $publishDirectory `
        '-m:1' `
        --verbosity minimal
    Invoke-DotNet publish $brokerProject `
        --configuration $Configuration `
        --no-restore `
        --output $publishDirectory `
        '-m:1' `
        --verbosity minimal

    Invoke-Signing @(
        (Join-Path $publishDirectory 'SmartLang.exe'),
        (Join-Path $publishDirectory 'SmartLang.Broker.exe'),
        (Join-Path $publishDirectory 'SmartLang.NativeHook.dll'),
        (Join-Path $publishDirectory 'SmartLang.NativeHook32.dll'),
        (Join-Path $publishDirectory 'SmartLang.NativeHost32.exe')
    )

    if (-not $SkipInstaller) {
        Write-Host "Building MSI to $installerDirectory..."
        Invoke-DotNet restore $installerProject '-m:1' --disable-parallel
        Invoke-DotNet build $installerProject `
            --configuration $Configuration `
            --no-restore `
            --output $installerDirectory `
            "-p:PublishDir=$publishDirectory" `
            '-m:1' `
            --verbosity minimal

        Invoke-Signing @(
            $installerPath
        )
    }

    Write-Host ''
    Write-Host "Build completed: SmartLang $effectiveVersion at $publishDirectory"
    if (-not $SkipInstaller) {
        Write-Host "Installer completed: $installerPath"
    }
}
finally {
    Pop-Location
}

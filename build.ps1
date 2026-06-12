[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutputDirectory = 'artifacts\publish',

    [switch] $SkipTests,

    [switch] $SkipInstaller,

    [string] $SigningCertificateThumbprint,

    [string] $TimestampUrl
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = $PSScriptRoot
$solution = Join-Path $repositoryRoot 'SmartLang.slnx'
$project = Join-Path $repositoryRoot 'SmartLang\SmartLang.csproj'
$brokerProject = Join-Path $repositoryRoot 'SmartLang.Broker\SmartLang.Broker.csproj'
$installerProject = Join-Path $repositoryRoot 'SmartLang.Installer\SmartLang.Installer.wixproj'
$publishDirectory = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $OutputDirectory))
$installerDirectory = Join-Path $repositoryRoot 'artifacts\installer'

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)

    & dotnet @Arguments
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
    Write-Host 'Restoring packages...'
    Invoke-DotNet restore $solution

    Write-Host "Building SmartLang and native x64/x86 hooks ($Configuration)..."
    Invoke-DotNet build $solution `
        --configuration $Configuration `
        --no-restore `
        --verbosity minimal

    if (-not $SkipTests) {
        Write-Host 'Running tests...'
        Invoke-DotNet test $solution `
            --configuration $Configuration `
            --no-build `
            --no-restore `
            --verbosity minimal
    }

    Write-Host "Publishing to $publishDirectory..."
    Invoke-DotNet publish $project `
        --configuration $Configuration `
        --no-restore `
        --output $publishDirectory `
        --verbosity minimal
    Invoke-DotNet publish $brokerProject `
        --configuration $Configuration `
        --no-restore `
        --output $publishDirectory `
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
        Invoke-DotNet restore $installerProject
        Invoke-DotNet build $installerProject `
            --configuration $Configuration `
            --no-restore `
            --output $installerDirectory `
            "-p:PublishDir=$publishDirectory" `
            --verbosity minimal

        Invoke-Signing @(
            (Join-Path $installerDirectory 'SmartLang.msi')
        )
    }

    Write-Host ''
    Write-Host "Build completed: $publishDirectory"
    if (-not $SkipInstaller) {
        Write-Host "Installer completed: $installerDirectory\SmartLang.msi"
    }
}
finally {
    Pop-Location
}

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutputDirectory = 'artifacts\publish',

    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = $PSScriptRoot
$solution = Join-Path $repositoryRoot 'SmartLang.slnx'
$project = Join-Path $repositoryRoot 'SmartLang\SmartLang.csproj'
$publishDirectory = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $OutputDirectory))

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
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

    Write-Host ''
    Write-Host "Build completed: $publishDirectory"
    Write-Host 'Deploy the entire folder; both native hook DLLs are required.'
}
finally {
    Pop-Location
}

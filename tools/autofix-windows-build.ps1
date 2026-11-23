<#
.SYNOPSIS
  Autofix the local Windows build environment for Operit2 (Plan B in BUILDING.md).

.DESCRIPTION
  Idempotently installs and aligns every toolchain the Windows Release Build
  requires, then runs the repository's own environment check to verify.

  Dry-run report (no changes):
    powershell -ExecutionPolicy Bypass -File tools\autofix-windows-build.ps1 -Check

  Apply fixes:
    powershell -ExecutionPolicy Bypass -File tools\autofix-windows-build.ps1

.PARAMETER Products
  Scope: app, cli, or all (default all). cli skips Dart/Flutter/TypeScript/.NET.
.PARAMETER CliArches
  host (default), all, x86_64, or aarch64. aarch64 requires LLVM and MSVC ARM64.
.PARAMETER Check
  Report current state and the actions that would run, without changing anything.
#>
[CmdletBinding()]
param(
    [ValidateSet('app', 'cli', 'all')]
    [string]$Products = 'all',

    [ValidateSet('host', 'all', 'x86_64', 'aarch64')]
    [string]$CliArches = 'host',

    [switch]$Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo       = Split-Path -Parent $PSScriptRoot
$FlutterApp = Join-Path $Repo 'apps\flutter\app'

$RustToolchain = '1.95.0'
$FvmVersion    = '4.3.0'
$TypeScriptVer = '5.9.3'
$NodeMajor     = '24'
$DotNetMajor   = '8'

$NeedApp      = $Products -in @('app', 'all')
$NeedCli      = $Products -in @('cli', 'all')
$NeedArm64Cli = $NeedCli -and ($CliArches -in @('all', 'aarch64'))

function Test-Cmd([string]$Name) {
    $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Info([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Ok([string]$Message)   { Write-Host "    [ok] $Message" -ForegroundColor Green }
function Warn([string]$Message) { Write-Host "    [warn] $Message" -ForegroundColor Yellow }

function Refresh-Path {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user    = [Environment]::GetEnvironmentVariable('Path', 'User')
    $cargo   = Join-Path $HOME '.cargo\bin'
    $pub     = Join-Path $env:APPDATA 'Pub\Cache\bin'
    $parts   = @($user, $machine, $cargo, $pub) | Where-Object { $_ }
    $env:Path = ($parts -join ';')
}

function Ensure-Rust {
    Info "Rust toolchain $RustToolchain + MSVC targets"
    if (-not (Test-Cmd 'rustup')) {
        if ($Check) { Warn 'rustup missing -> would run: winget install -e --id Rustlang.Rustup'; return }
        winget install -e --id Rustlang.Rustup --accept-source-agreements --accept-package-agreements --silent
        Refresh-Path
        if (-not (Test-Cmd 'rustup')) { throw 'rustup was not found on PATH after install' }
    }
    $toolchains = @(& rustup toolchain list 2>$null)
    if ($toolchains -match "^$([regex]::Escape($RustToolchain))") {
        Ok "rustup toolchain $RustToolchain"
    } elseif ($Check) {
        Warn "toolchain $RustToolchain missing -> would run: rustup toolchain install $RustToolchain"
    } else {
        & rustup toolchain install $RustToolchain --profile minimal
    }
    if (-not $Check) { & rustup default $RustToolchain }
    foreach ($target in @('x86_64-pc-windows-msvc', 'aarch64-pc-windows-msvc')) {
        $have = @(& rustup target list --installed 2>$null)
        if ($have -contains $target) { Ok "rust target $target" }
        elseif ($Check) { Warn "rust target $target missing -> would run: rustup target add $target" }
        else { & rustup target add $target }
    }
}

function Ensure-Node {
    Info "Node.js $NodeMajor + corepack"
    $version = if (Test-Cmd 'node') { (& node --version 2>$null) } else { '' }
    $major = if ($version -match '^v(\d+)\.') { $Matches[1] } else { '' }
    if ($major -eq $NodeMajor) {
        Ok "Node.js $version"
    } elseif ($Check) {
        Warn "Node.js $NodeMajor missing (found: $version) -> would run: winget install -e --id OpenJS.NodeJS.LTS"
    } else {
        Warn "Installing Node.js $NodeMajor LTS (replaces $version)"
        winget install -e --id OpenJS.NodeJS.LTS --accept-source-agreements --accept-package-agreements --silent
        Refresh-Path
    }
    if (Test-Cmd 'corepack') {
        Ok 'corepack available'
    } elseif ($Check) {
        Warn 'corepack missing -> would run: corepack enable'
    } else {
        $cp = Get-Command corepack -ErrorAction SilentlyContinue
        if ($cp) { & corepack enable; Ok 'corepack enabled' }
        else { Warn 'corepack still missing; run "corepack enable" after Node 24 is active' }
    }
}

function Ensure-TypeScript {
    Info "TypeScript $TypeScriptVer"
    $tsc = Join-Path $Repo '.ci-tools\typescript\node_modules\.bin\tsc.cmd'
    if (Test-Path $tsc) {
        Ok $tsc
    } elseif ($Check) {
        Warn "TypeScript missing -> npm install --prefix .ci-tools/typescript --no-audit --no-fund typescript@$TypeScriptVer"
    } else {
        & npm install --prefix (Join-Path $Repo '.ci-tools\typescript') --no-audit --no-fund "typescript@$TypeScriptVer"
    }
}

function Ensure-DartFvm {
    Info "Dart + FVM $FvmVersion + pinned Flutter SDK"
    if (-not (Test-Cmd 'dart')) {
        if ($Check) { Warn 'dart missing -> would run: choco install dart-sdk -y'; return }
        if (Test-Admin) { choco install dart-sdk -y --no-progress --limit-output; Refresh-Path }
        else { Warn 'choco install dart-sdk requires an elevated shell; re-run as Administrator'; return }
    }
    if (Test-Cmd 'fvm') {
        Ok 'fvm'
    } elseif ($Check) {
        Warn "fvm missing -> would run: dart pub global activate fvm $FvmVersion"
    } else {
        & dart pub global activate fvm $FvmVersion
        Refresh-Path
    }
    $flutter = Join-Path $FlutterApp '.fvm\flutter_sdk\bin\flutter.bat'
    if (Test-Path $flutter) {
        Ok 'pinned Flutter SDK'
    } elseif ($Check) {
        Warn 'pinned Flutter SDK missing -> would run: fvm install --skip-pub-get'
    } else {
        Push-Location $FlutterApp
        try { & fvm install --skip-pub-get } finally { Pop-Location }
    }
}

function Ensure-LLVM {
    Info 'LLVM clang (aarch64 CLI and native bindgen)'
    $clang = 'C:\Program Files\LLVM\bin\clang.exe'
    if (Test-Path $clang) {
        Ok $clang
    } elseif ($Check) {
        Warn 'LLVM clang missing -> would run: choco install llvm -y (admin)'
    } elseif (Test-Admin) {
        choco install llvm -y --no-progress --limit-output
    } else {
        Warn 'choco install llvm requires an elevated shell; re-run as Administrator'
    }
}

function Ensure-DotNet {
    Info ".NET SDK $DotNetMajor.x (MSI packaging)"
    $sdks = if (Test-Cmd 'dotnet') { @(& dotnet --list-sdks 2>$null) } else { @() }
    $has = $sdks | Where-Object { $_ -match '^8\.\d+\.\d+ ' }
    if ($has) {
        Ok ($has | Select-Object -First 1)
    } elseif ($Check) {
        Warn ".NET SDK $DotNetMajor missing -> would run: winget install -e --id Microsoft.DotNet.SDK.8"
    } else {
        winget install -e --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements --silent
        Refresh-Path
    }
}

function Ensure-VsArm64 {
    Info 'Visual Studio MSVC ARM64 component'
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { Warn 'vswhere not found; cannot verify MSVC ARM64'; return }
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath 2>$null
    if ($vs) { Ok 'MSVC ARM64 component present' }
    elseif ($Check) { Warn 'MSVC ARM64 missing -> add Microsoft.VisualStudio.Component.VC.Tools.ARM64 via VS Installer' }
    else { Warn 'MSVC ARM64 missing; add component Microsoft.VisualStudio.Component.VC.Tools.ARM64 in the Visual Studio Installer, then re-run' }
}

function Ensure-PythonVenv {
    Info 'Python virtual environment'
    $venvPython = Join-Path $Repo '.venv\Scripts\python.exe'
    if (Test-Path $venvPython) {
        Ok $venvPython
    } elseif ($Check) {
        Warn 'venv missing -> would run: python -m venv .venv'
    } else {
        & python -m venv (Join-Path $Repo '.venv')
    }
}

function Invoke-Verify {
    Info 'Verifying with check_build_environment.py'
    $venvPython = Join-Path $Repo '.venv\Scripts\python.exe'
    $python = if (Test-Path $venvPython) { $venvPython } else { (Get-Command python -ErrorAction SilentlyContinue).Source }
    if (-not $python) { Warn 'python not found; skipping verification'; return }
    & $python (Join-Path $Repo 'tools\build_scripts\check_build_environment.py') --products $Products --cli-arches $CliArches
    if ($LASTEXITCODE -eq 0) { Ok 'environment check passed' }
    else { Warn "environment check failed (exit $LASTEXITCODE)" }
}

function Main {
    if (-not (Test-Cmd 'winget')) { Warn 'winget not found on PATH; package installs will be skipped' }
    if (-not (Test-Admin)) { Warn 'not running elevated; chocolatey installs (LLVM, dart) will be skipped' }
    Info "Operit2 Windows build autofix (products=$Products, cli_arches=$CliArches)$(if ($Check) { ' [DRY-RUN]' })"
    Ensure-Rust
    Ensure-Node
    if ($NeedApp) {
        Ensure-TypeScript
        Ensure-DartFvm
        Ensure-DotNet
        Ensure-PythonVenv
    }
    Ensure-LLVM
    if ($NeedArm64Cli) { Ensure-VsArm64 }
    if ($Check) {
        Info 'Dry-run complete. Run without -Check to apply fixes.'
    } else {
        Invoke-Verify
    }
}

Main

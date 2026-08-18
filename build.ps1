# Compila Sonda e produce:
#   dist\portable\Sonda.exe            eseguibile singolo, self-contained (non serve .NET installato)
#   dist\Sonda-<versione>-portable.zip lo stesso, zippato
#   dist\Sonda-<versione>-Setup.exe    installer Inno Setup
#
# Uso:
#   .\build.ps1                 tutto (portable + installer)
#   .\build.ps1 -SoloPortable   solo l'eseguibile portable
#   .\build.ps1 -SoloInstaller  solo l'installer (usa il portable gia' compilato)
#   .\build.ps1 -Firma          firma i binari (serve un certificato: vedi SONDA_FIRMA_PS1 sotto)
#
# Prerequisiti: .NET SDK 9 (winget install Microsoft.DotNet.SDK.9), Inno Setup 6 per l'installer.

param(
    [switch] $SoloPortable,
    [switch] $SoloInstaller,
    [switch] $Firma
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$portable = Join-Path $dist 'portable'

# versione dal csproj
[xml]$proj = Get-Content (Join-Path $root 'Sonda.csproj')
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { $version = '1.0.0' }
Write-Host "Sonda $version" -ForegroundColor Cyan

# dotnet: dopo un'installazione fresca puo' non essere ancora nel PATH della sessione
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { throw "dotnet non trovato: installa l'SDK .NET 9 (winget install Microsoft.DotNet.SDK.9)" }

# Firma Authenticode (facoltativa). Lo script di firma non fa parte del progetto: si indica con una
# variabile d'ambiente, cosi' ognuno usa il proprio certificato.
#   SONDA_FIRMA_PS1   script PowerShell che firma i file passati con -File
#   SONDA_FIRMA_CMD   comando che Inno Setup chiama per firmare setup e disinstallatore ($f = file)
$firmaScript = if ($env:SONDA_FIRMA_PS1) { $env:SONDA_FIRMA_PS1 } else { 'firma.ps1' }

if (-not $SoloInstaller) {
    # icona (se manca)
    if (-not (Test-Path (Join-Path $root 'Resources\icon.ico'))) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Resources\crea-icona.ps1')
    }

    Write-Host "== publish (win-x64, self-contained, single-file)" -ForegroundColor Yellow
    # dist pulita: niente setup/zip di versioni vecchie accanto ai nuovi
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    New-Item -ItemType Directory -Force $portable | Out-Null
    & $dotnet publish (Join-Path $root 'Sonda.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -p:DebugSymbols=false `
        -o $portable --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "publish fallito" }

    # tieni solo l'exe (il publish single-file non lascia altro, ma per sicurezza)
    Get-ChildItem $portable -Exclude 'Sonda.exe' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root 'LEGGIMI.txt') $portable -ErrorAction SilentlyContinue

    $exe = Join-Path $portable 'Sonda.exe'
    $mb = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "   $exe  ($mb MB)" -ForegroundColor Green

    if ($Firma) {
        if (-not (Test-Path $firmaScript)) { throw "-Firma richiesto ma manca $firmaScript" }
        Write-Host "== firma" -ForegroundColor Yellow
        & $firmaScript -File $exe -Descrizione 'Sonda'
        if ($LASTEXITCODE -ne 0) { throw "firma fallita" }
    }

    $zip = Join-Path $dist "Sonda-$version-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "   $zip" -ForegroundColor Green
}

if (-not $SoloPortable) {
    Write-Host "== installer (Inno Setup)" -ForegroundColor Yellow
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) non trovato" }
    if (-not (Test-Path (Join-Path $portable 'Sonda.exe'))) { throw "manca dist\portable\Sonda.exe: esegui prima .\build.ps1 senza -SoloInstaller" }

    # Con -Firma la firma la fa Inno stesso (SignTool + SignedUninstaller), tramite SONDA_FIRMA_CMD:
    # cosi' anche il disinstallatore, generato sul PC dell'utente, esce firmato.
    $isccArgs = @('/Q', "/DAppVersion=$version")
    $firmaCmd = if ($env:SONDA_FIRMA_CMD) { $env:SONDA_FIRMA_CMD } else { 'firma-inno.cmd' }
    if ($Firma) {
        if (-not (Test-Path $firmaCmd)) { throw "-Firma richiesto ma manca $firmaCmd" }
        $isccArgs += '/DSign'
        $isccArgs += "/Sstarverb=`"$firmaCmd`" `$f"
    }
    & $iscc @isccArgs (Join-Path $root 'Installer\Sonda.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC fallito" }
    $setup = Join-Path $dist "Sonda-$version-Setup.exe"
    Write-Host "   $setup" -ForegroundColor Green
}

Write-Host "Fatto." -ForegroundColor Cyan

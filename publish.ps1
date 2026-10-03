<#
.SYNOPSIS
    Testar, publicerar SnapShot med Native AOT till G:\Apps\SnapShot och lägger den som autostart.

.DESCRIPTION
    1. Kör enhetstesterna; faller de publiceras ingenting.
    2. Stoppar en körande SnapShot från målmappen (exe:n är låst medan den körs).
    3. Publicerar till en mellanmapp och kopierar bara SnapShot.exe till målmappen.
    4. Skriver Run-nyckeln i HKCU så att appen startar med Windows.
    5. Startar appen.

.PARAMETER Destination
    Mappen appen installeras i.

.PARAMETER NoStart
    Starta inte appen efter publiceringen.
#>
[CmdletBinding()]
param(
    [string]$Destination = 'G:\Apps\SnapShot',
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\SnapShot\SnapShot.csproj'
$staging = Join-Path $root 'artifacts\publish'
$exePath = Join-Path $Destination 'SnapShot.exe'

# Samma värde som Autostart.cs skriver, så att bocken i menyn visar rätt.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'SnapShot'
$runCommand = "`"$exePath`""

Write-Host '==> Testar' -ForegroundColor Cyan
dotnet test (Join-Path $root 'SnapShot.slnx') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Testerna gick inte igenom; ingenting publicerades.' }

Write-Host '==> Publicerar (Native AOT)' -ForegroundColor Cyan

# Är variabeln satt (vissa agent-skal gör det) hittar VS-skriptet inte vswhere.exe och
# länkningen faller. Den tas bort bara under publiceringen.
$savedExeLookup = $env:NoDefaultCurrentDirectoryInExePath
Remove-Item Env:NoDefaultCurrentDirectoryInExePath -ErrorAction SilentlyContinue

try {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    dotnet publish $project -c Release -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'Publiceringen misslyckades.' }
}
finally {
    if ($null -ne $savedExeLookup) { $env:NoDefaultCurrentDirectoryInExePath = $savedExeLookup }
}

Write-Host "==> Installerar i $Destination" -ForegroundColor Cyan
$running = Get-Process -Name SnapShot -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ($_.Path -ieq $exePath) }
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item (Join-Path $staging 'SnapShot.exe') $exePath -Force

Write-Host '==> Autostart' -ForegroundColor Cyan
# Inte New-Item -Force: på en befintlig nyckel återskapar det nyckeln och tar bort alla
# andra programs autostart-poster.
if (-not (Test-Path $runKey)) { New-Item -Path $runKey | Out-Null }
Set-ItemProperty -Path $runKey -Name $runValueName -Value $runCommand

if (-not $NoStart) {
    Write-Host '==> Startar' -ForegroundColor Cyan
    Start-Process $exePath
}

$sizeMb = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
Write-Host "Klart: $exePath ($sizeMb MB), startar med Windows." -ForegroundColor Green

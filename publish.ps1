# Build / publish DoneToday and (re)launch.
#
# Usage:
#   .\publish.ps1                          # build Release, relaunch
#   .\publish.ps1 -BumpVersion             # bump patch (e.g. 0.1.0 -> 0.1.1), build, relaunch
#   .\publish.ps1 -BumpVersion -BumpType minor   # bump minor (0.1.0 -> 0.2.0)
#   .\publish.ps1 -BumpVersion -BumpType major   # bump major
#   .\publish.ps1 -SetVersion 1.2.3        # set an explicit version
#   .\publish.ps1 -Publish                 # produce a single-file .exe in dist\ (still .NET-runtime-dependent)
#   .\publish.ps1 -NoLaunch                # build but don't open the app

[CmdletBinding()]
param(
    [switch]$BumpVersion,
    [ValidateSet('major','minor','patch')]
    [string]$BumpType = 'patch',
    [string]$SetVersion,
    [switch]$Publish,
    [switch]$NoLaunch,
    [string]$Distribute,           # folder to drop the new .exe + latest.json into
    [string]$ReleaseNotes          # optional notes embedded in latest.json
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csproj = Join-Path $root "DoneToday.csproj"

# 1. Stop any running instance so the .exe isn't locked
$running = Get-Process DoneToday -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running DoneToday..." -ForegroundColor DarkGray
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 250
}

# 2. Read current version, bump or replace if requested
[xml]$xml = Get-Content $csproj
$pg = ($xml.Project.PropertyGroup | Where-Object { $_.Version }) | Select-Object -First 1
if (-not $pg) { $pg = $xml.Project.PropertyGroup | Select-Object -First 1 }

$currentVersion = "$($pg.Version)"
if (-not $currentVersion) { $currentVersion = '0.1.0' }

$newVersion = $currentVersion
if ($SetVersion) {
    $newVersion = $SetVersion
} elseif ($BumpVersion) {
    $parts = $currentVersion -split '\.'
    while ($parts.Count -lt 3) { $parts += '0' }
    $major = [int]$parts[0]; $minor = [int]$parts[1]; $patch = [int]$parts[2]
    switch ($BumpType) {
        'major' { $major++; $minor = 0; $patch = 0 }
        'minor' { $minor++; $patch = 0 }
        'patch' { $patch++ }
    }
    $newVersion = "$major.$minor.$patch"
}

if ($newVersion -ne $currentVersion) {
    $pg.Version         = $newVersion
    $pg.AssemblyVersion = "$newVersion.0"
    $pg.FileVersion     = "$newVersion.0"
    $xml.Save($csproj)
    Write-Host "Version: $currentVersion -> $newVersion" -ForegroundColor Cyan
} else {
    Write-Host "Version: $currentVersion" -ForegroundColor DarkGray
}

# 3. Build (or single-file publish)
Push-Location $root
try {
    if ($Publish) {
        Write-Host "Publishing single-file..." -ForegroundColor Cyan
        & dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "dist"
        if ($LASTEXITCODE -ne 0) { throw "publish failed" }
        $exe = Join-Path $root "dist\DoneToday.exe"
    } else {
        Write-Host "Building Release..." -ForegroundColor Cyan
        & dotnet build -c Release
        if ($LASTEXITCODE -ne 0) { throw "build failed" }
        $exe = Join-Path $root "bin\Release\net10.0-windows\DoneToday.exe"
    }

    if (Test-Path $exe) {
        Write-Host "Built: $exe" -ForegroundColor Green
    } else {
        throw "expected .exe not found at $exe"
    }

    if ($Distribute) {
        if (-not (Test-Path $Distribute)) { New-Item -ItemType Directory -Path $Distribute | Out-Null }
        $namedExe = "DoneToday-$newVersion.exe"
        $destExe = Join-Path $Distribute $namedExe
        Copy-Item -LiteralPath $exe -Destination $destExe -Force

        $manifest = [ordered]@{
            version = $newVersion
            exe     = $namedExe
            notes   = $ReleaseNotes
        }
        $manifestPath = Join-Path $Distribute "latest.json"
        $manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8
        Write-Host "Distributed -> $destExe" -ForegroundColor Green
        Write-Host "Manifest    -> $manifestPath" -ForegroundColor Green
    }

    if (-not $NoLaunch) {
        Start-Process $exe
        Write-Host "Launched." -ForegroundColor Green
    }
}
finally {
    Pop-Location
}

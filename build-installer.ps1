param(
    [ValidateSet("win-x64")][string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$Project = Join-Path $PSScriptRoot "Szperacz\Szperacz.csproj"
$PublishDir = Join-Path $PSScriptRoot "publish\$Runtime"
$DistDir = Join-Path $PSScriptRoot "dist"

Write-Host "== OGŚ: publikowanie aplikacji Szperacz 1.6.1 PRO ==" -ForegroundColor Cyan
dotnet publish $Project -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish nie powiódł się." }

$Iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if (-not $Iscc) {
    $Candidates = @(
        "$env:ProgramFiles(x86)\Inno Setup 7\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LocalAppData\Programs\Inno Setup 7\ISCC.exe",
        "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $Candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
} else {
    $IsccPath = $Iscc.Source
}

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

if (-not $IsccPath) {
    Write-Host "Aplikacja została opublikowana w: $PublishDir" -ForegroundColor Yellow
    Write-Host "Kompilator ISCC.exe nie został automatycznie wykryty. Uruchom Inno Setup ręcznie dla Installer\Szperacz.iss." -ForegroundColor Yellow
    exit 0
}

Write-Host "== OGŚ: budowanie instalatora Szperacz przez $IsccPath ==" -ForegroundColor Cyan
& $IsccPath (Join-Path $PSScriptRoot "Installer\Szperacz.iss")
if ($LASTEXITCODE -ne 0) { throw "Kompilacja instalatora nie powiodła się." }
Write-Host "Gotowe! Instalator Szperacz-Setup-1.6.1-x64.exe znajduje się w folderze dist." -ForegroundColor Green

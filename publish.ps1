# Builds dist\SackSpace.exe: one self-contained file (the PC needs no .NET installed),
# then the wizard dist\SackSpace-Setup-<version>.exe.
# Usage: .\publish.ps1                 tests, exe and wizard (on this PC)
#        .\publish.ps1 -NoInstaller    tests and exe only (CI: the exe is signed before it goes into the wizard)
#        .\publish.ps1 -InstallerOnly  wizard from the exe already in dist (CI, after signing it)
param([switch]$NoInstaller, [switch]$InstallerOnly)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$out = Join-Path $PSScriptRoot "dist"

if (-not $InstallerOnly) {
    dotnet test SackSpace.slnx -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Las pruebas fallaron; no se publica." }

    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    dotnet publish src/SackSpace/SackSpace.csproj -c Release -p:PublishSingleFile=true -o $out --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló." }

    Remove-Item (Join-Path $out "*.pdb") -ErrorAction SilentlyContinue
    $exe = Get-Item (Join-Path $out "SackSpace.exe")
    "Listo: {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)
}
if ($NoInstaller) { return }

# Wizard de instalación (Inno Setup 6: winget install JRSoftware.InnoSetup).
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    if ($InstallerOnly) { throw "Sin Inno Setup: no se puede generar el wizard." }
    Write-Warning "Sin Inno Setup: no se genera el wizard."; return
}
& $iscc /Q (Join-Path $PSScriptRoot "installer\SackSpace.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC falló." }
Get-ChildItem $out -Filter "SackSpace-Setup-*.exe" | ForEach-Object { "Wizard: {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }

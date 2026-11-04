# Builds dist\SackSpace.exe: one self-contained file (the PC needs no .NET installed).
# Usage: .\publish.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet test SackSpace.slnx -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Las pruebas fallaron; no se publica." }

$out = Join-Path $PSScriptRoot "dist"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src/SackSpace/SackSpace.csproj -c Release -p:PublishSingleFile=true -o $out --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló." }

Remove-Item (Join-Path $out "*.pdb") -ErrorAction SilentlyContinue
$exe = Get-Item (Join-Path $out "SackSpace.exe")
"Listo: {0} ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)


# Wizard de instalación (Inno Setup 6: winget install JRSoftware.InnoSetup).
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Write-Warning "Sin Inno Setup: no se genera el wizard."; return }
& $iscc /Q (Join-Path $PSScriptRoot "installer\SackSpace.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC falló." }
Get-ChildItem $out -Filter "SackSpace-Setup-*.exe" | ForEach-Object { "Wizard: {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }

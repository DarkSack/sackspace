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


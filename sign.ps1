# Signs one file in place through the SignPath REST API (PowerShell module "SignPath").
# Used by .github/workflows/build.yml. Reads SIGNPATH_API_TOKEN, SIGNPATH_ORGANIZATION_ID,
# SIGNPATH_PROJECT and SIGNPATH_POLICY from the environment.
# The artifact configurations in .signpath/ have a <zip-file> root (what the GitHub
# connector uploads), so the file goes into a zip and comes back out of one.
param(
    [Parameter(Mandatory)] [string]$File,
    [Parameter(Mandatory)] [string]$Configuration
)
$ErrorActionPreference = 'Stop'

foreach ($name in 'SIGNPATH_API_TOKEN', 'SIGNPATH_ORGANIZATION_ID', 'SIGNPATH_PROJECT', 'SIGNPATH_POLICY') {
    if (-not [Environment]::GetEnvironmentVariable($name)) { throw "Falta la variable de entorno $name" }
}

$file = Get-Item $File
$work = Join-Path ([IO.Path]::GetTempPath()) ("signpath-" + [guid]::NewGuid())
New-Item -ItemType Directory $work | Out-Null
$unsigned = Join-Path $work 'unsigned.zip'
$signed = Join-Path $work 'signed.zip'

Compress-Archive -Path $file.FullName -DestinationPath $unsigned
Submit-SigningRequest `
    -InputArtifactPath $unsigned `
    -ApiToken $env:SIGNPATH_API_TOKEN `
    -OrganizationId $env:SIGNPATH_ORGANIZATION_ID `
    -ProjectSlug $env:SIGNPATH_PROJECT `
    -SigningPolicySlug $env:SIGNPATH_POLICY `
    -ArtifactConfigurationSlug $Configuration `
    -OutputArtifactPath $signed `
    -WaitForCompletion

Expand-Archive -Path $signed -DestinationPath (Join-Path $work 'out')
Copy-Item (Join-Path $work "out/$($file.Name)") $file.FullName -Force
Remove-Item $work -Recurse -Force

# The test certificate is not trusted by Windows, so only check that a signature is there.
$signature = Get-AuthenticodeSignature $file.FullName
if (-not $signature.SignerCertificate) { throw "$($file.Name) volvio de SignPath sin firma" }
"$($file.Name) firmado por $($signature.SignerCertificate.Subject) ($($signature.Status))"

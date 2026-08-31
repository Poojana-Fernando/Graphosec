$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installerScript = Join-Path $repositoryRoot "installer\ResumableCopy.iss"
$setupOutput = Join-Path $repositoryRoot "artifacts\installer\Graphosec-Setup-1.0.0.exe"
$copyEngineRoot = Join-Path (Split-Path -Parent $repositoryRoot) "Graphosec-Copy-Engine"
$isccCandidates = @(
    "${env:ProgramFiles}\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

Write-Host "Building Graphosec self-contained publish..."
& (Join-Path $repositoryRoot "installer\publish-release.ps1")

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6 or 7."
}

Write-Host "Compiling installer with $iscc ..."
& $iscc $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compile failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $setupOutput)) {
    throw "Installer output was not found at $setupOutput"
}

if (-not (Test-Path $copyEngineRoot)) {
    throw "Graphosec-Copy-Engine repo was not found at $copyEngineRoot"
}

Copy-Item $setupOutput (Join-Path $copyEngineRoot "Graphosec-Setup-1.0.0.exe") -Force
Write-Host "Copied installer to Graphosec-Copy-Engine repo."
Write-Host "Next: commit, tag (for example v1.0.0), and push to publish a GitHub Release."

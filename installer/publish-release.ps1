$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $repositoryRoot "artifacts/publish/Graphosec"

Write-Host "Publishing Graphosec (Release, win-x64, self-contained)..."
dotnet publish (Join-Path $repositoryRoot "src/ResumableCopy.App/ResumableCopy.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=false `
    /p:NuGetAudit=false `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

Write-Host "Publish complete: $publishDirectory"

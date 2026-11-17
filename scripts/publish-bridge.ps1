# Builds the headless Discord bridge for every platform WaNut deploys it to, one trimmed self-contained file each,
# into artifacts/bridge/<rid>/. Building WaNut.App afterwards copies them into the app folder as bridge\<rid>\, so
# the installer carries all three. Run from anywhere: pwsh scripts/publish-bridge.ps1 [-Version 1.2.3]
param(
    [string]$Version = '',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts/bridge'

foreach ($rid in 'win-x64', 'linux-x64', 'linux-arm64') {
    $target = Join-Path $out $rid
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    $arguments = @('publish', (Join-Path $root 'src/WaNut.Bridge'), '-c', $Configuration, '-r', $rid, '-o', $target)
    if ($Version) { $arguments += "-p:Version=$Version" }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publishing the bridge for $rid failed." }
    Get-ChildItem $target | ForEach-Object { "{0}  {1:N1} MB" -f $_.FullName, ($_.Length / 1MB) }
}

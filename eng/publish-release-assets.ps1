[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BundlePath,
    [Parameter(Mandatory)] [string] $Tag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$headers = @{ Authorization = "Bearer $($env:GH_TOKEN)"; 'X-GitHub-Api-Version' = '2022-11-28' }
$api = "$($env:GITHUB_API_URL)/repos/$($env:GITHUB_REPOSITORY)"
$releaseId = (& gh release view $Tag --json databaseId --jq .databaseId)
if ($LASTEXITCODE -ne 0 -or $releaseId -cnotmatch '^[0-9]+$') { throw 'Could not resolve the release or draft ID.' }
$release = Invoke-RestMethod -Uri "$api/releases/$releaseId" -Headers $headers
$files = @(Get-ChildItem -LiteralPath $BundlePath -File | Where-Object { $_.Extension -eq '.nupkg' -or $_.Name -eq 'sbom.cdx.json' })
$pending = @()
foreach ($file in $files) {
    $assets = @($release.assets | Where-Object name -CEQ $file.Name)
    if ($assets.Count -eq 0) {
        if (-not $release.draft) { throw "Published immutable release is missing $($file.Name); manual recovery is required." }
        $pending += $file
        continue
    }
    if ($assets.Count -ne 1) { throw "Ambiguous existing release asset: $($file.Name)." }
    $download = Join-Path $env:RUNNER_TEMP ('existing-asset-' + [guid]::NewGuid().ToString('N'))
    $assetHeaders = $headers.Clone()
    $assetHeaders.Accept = 'application/octet-stream'
    Invoke-WebRequest -Uri "$api/releases/assets/$($assets[0].id)" -Headers $assetHeaders -OutFile $download
    if ((Get-FileHash $download -Algorithm SHA256).Hash -cne (Get-FileHash $file.FullName -Algorithm SHA256).Hash) {
        throw "Existing release asset bytes differ: $($file.Name)."
    }
}
foreach ($file in $pending) {
    & gh release upload $Tag $file.FullName
    if ($LASTEXITCODE -ne 0) { throw "Release asset upload stopped at $($file.Name); rerun to recover." }
}
if ($release.draft) {
    & gh release edit $Tag --draft=false
    if ($LASTEXITCODE -ne 0) { throw 'Assets are uploaded, but draft publication failed; rerun to recover.' }
}

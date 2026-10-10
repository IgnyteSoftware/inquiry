[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$headers = @{ Authorization = "Bearer $($env:GH_TOKEN)"; 'X-GitHub-Api-Version' = '2022-11-28' }
$api = "$($env:GITHUB_API_URL)/repos/$($env:GITHUB_REPOSITORY)"
$artifacts = @()
$page = 1
while ($true) {
    $batch = Invoke-RestMethod -Uri "$api/actions/runs/$($env:GITHUB_RUN_ID)/artifacts?per_page=100&page=$page" -Headers $headers
    $artifacts += @($batch.artifacts | Where-Object name -CEQ $Name)
    if ($batch.artifacts.Count -lt 100) { break }
    $page++
}
if ($artifacts.Count -eq 0) {
    'restored=false' >> $env:GITHUB_OUTPUT
    return
}
if ($artifacts.Count -ne 1 -or $artifacts[0].expired -or $artifacts[0].digest -cnotmatch '^sha256:[0-9a-f]{64}$') {
    throw 'The original candidate artifact is ambiguous, expired, or has no verified digest.'
}
$artifact = $artifacts[0]
$archivePath = Join-Path $env:RUNNER_TEMP 'original-release-candidate.zip'
Invoke-WebRequest -Uri "$api/actions/artifacts/$($artifact.id)/zip" -Headers $headers -OutFile $archivePath
if ('sha256:' + (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $artifact.digest) {
    throw 'The stored release candidate archive digest differs.'
}
Expand-Archive -LiteralPath $archivePath -DestinationPath $OutputPath
'restored=true' >> $env:GITHUB_OUTPUT
Write-Host "Restored original candidate artifact $($artifact.id) ($($artifact.digest))."

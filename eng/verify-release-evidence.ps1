[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BundlePath,
    [Parameter(Mandatory)] [string] $EvidencePath,
    [Parameter(Mandatory)] [string] $Commit,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $RunId,
    [Parameter(Mandatory)] [string] $RunAttempt,
    [Parameter(Mandatory)] [string] $ArtifactId,
    [Parameter(Mandatory)] [string] $ArtifactDigest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($ArtifactId -cnotmatch '^[0-9]+$' -or $ArtifactDigest -cnotmatch '^[0-9a-f]{64}$') {
    throw 'Release evidence requires the immutable candidate artifact identity.'
}
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -Raw (Join-Path $root 'eng/release-manifest.json') | ConvertFrom-Json
$stable = $Version -ceq $manifest.packageVersion
if (-not $stable -and $Version -cnotmatch ('^' + [regex]::Escape($manifest.packageVersion) + '-preview\.[0-9]+$')) {
    throw 'The release version must match the manifest or its numbered preview.'
}
if ($stable) {
    foreach ($package in $manifest.packages) {
        $directory = Split-Path -Parent (Join-Path $root $package.project)
        $unshipped = @(Get-Content (Join-Path $directory 'PublicAPI.Unshipped.txt') | Where-Object { $_.Trim() -and -not $_.StartsWith('#') })
        $shipped = @(Get-Content (Join-Path $directory 'PublicAPI.Shipped.txt') | Where-Object { $_.Trim() -and -not $_.StartsWith('#') })
        if ($unshipped.Count -ne 0 -or $shipped.Count -eq 0) { throw "Stable API freeze is incomplete: $($package.id)." }
    }
}
$evidenceFile = Join-Path $EvidencePath 'package-consumers.json'
$evidence = Get-Content -Raw $evidenceFile | ConvertFrom-Json
if ($evidence.schemaVersion -cne 'inquiry-package-consumers-v1' -or $evidence.status -cne 'passed' -or
    $evidence.commit -cne $Commit -or $evidence.packageVersion -cne $Version) {
    throw 'Package consumer evidence does not match this release.'
}
$expectedChecks = @(
    foreach ($tfm in @('net8.0', 'net9.0', 'net10.0')) {
        foreach ($provider in @('MariaDb', 'MySql', 'Oracle', 'PostgreSql', 'Sqlite', 'SqlServer')) { "$tfm/$provider/analyzer-and-generated-crud-build" }
        "$tfm/sqlite/crud"
        "$tfm/companions"
    }
    'net10.0/linux-x64/nativeaot-crud'
)
if (@(Compare-Object ($expectedChecks | Sort-Object) ($evidence.checks | Sort-Object)).Count -ne 0) {
    throw 'Package consumer checks are incomplete.'
}
$files = @(Get-ChildItem -LiteralPath $BundlePath -File | Sort-Object Name)
if (@(Compare-Object ($files.Name | Sort-Object) ($evidence.packages.file | Sort-Object)).Count -ne 0) {
    throw 'Release inventory differs from verified consumer evidence.'
}
foreach ($file in $files) {
    $record = @($evidence.packages | Where-Object file -CEQ $file.Name)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($record.Count -ne 1 -or $record[0].sha256 -cne $hash) { throw "Unverified release bytes: $($file.Name)." }
}
# Job dependencies supply these outcomes; never infer success from the absence of a failed check.
if ($env:RELEASE_CORRECTNESS_RESULT -cne 'success' -or $env:RELEASE_SECURITY_RESULT -cne 'success') {
    throw 'Exact-commit correctness and security jobs must both succeed.'
}
@{
    schemaVersion = 'inquiry-release-evidence-v1'
    commit = $Commit
    version = $Version
    runId = $RunId
    runAttempt = $RunAttempt
    candidateArtifactId = $ArtifactId
    candidateArtifactDigest = $ArtifactDigest
    correctness = $env:RELEASE_CORRECTNESS_RESULT
    securityWorkflowResult = $env:RELEASE_SECURITY_RESULT
    codeql = 'analysis-completed'
    dependencyAudit = 'passed'
    codeqlAlertDisposition = 'requires-release-review'
    apiPolicy = $(if ($stable) { 'shipped-api-freeze' } else { 'preview-unshipped-allowed' })
    consumerEvidenceSha256 = (Get-FileHash -LiteralPath $evidenceFile -Algorithm SHA256).Hash.ToLowerInvariant()
    files = $evidence.packages
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $EvidencePath 'release-evidence.json') -Encoding utf8
Write-Host 'Release evidence matches the candidate bytes.'

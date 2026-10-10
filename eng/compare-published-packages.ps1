[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BundlePath,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutputPath,
    [switch] $RequireEmptyRegistries
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -Raw (Join-Path $PSScriptRoot 'release-manifest.json') | ConvertFrom-Json
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('inquiry-registry-preflight-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null

function Get-Payload([string] $Path) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
        $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            if (-not $names.Add($entry.FullName) -or $entry.FullName.Contains('\') -or
                $entry.FullName.StartsWith('/') -or @($entry.FullName.Split('/') | Where-Object { $_ -in @('.', '..') }).Count -gt 0) {
                throw 'Package contains a duplicate or unsafe archive path.'
            }
            if ($entry.FullName -ceq '.signature.p7s') { continue }
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            $entries.Add($entry.FullName, $hash)
        }
        return $entries
    }
    finally { $archive.Dispose() }
}

$githubCredentials = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("$($env:GITHUB_ACTOR):$($env:GITHUB_TOKEN)"))
$registries = @(
    @{ name = 'nuget'; index = 'https://api.nuget.org/v3/index.json'; headers = @{} },
    @{ name = 'github'; index = 'https://nuget.pkg.github.com/IgnyteSoftware/index.json'; headers = @{ Authorization = "Basic $githubCredentials" } }
)
$plan = @()
foreach ($registry in $registries) {
    $index = Invoke-RestMethod -Uri $registry.index -Headers $registry.headers
    $base = @($index.resources | Where-Object { $_.'@type' -eq 'PackageBaseAddress/3.0.0' })
    if ($base.Count -ne 1 -or -not $base[0].'@id'.StartsWith('https://')) { throw 'Registry has no unique HTTPS package download endpoint.' }
    foreach ($package in $manifest.packages) {
        $id = $package.id.ToLowerInvariant()
        $lowerVersion = $Version.ToLowerInvariant()
        $candidate = Join-Path $BundlePath "$($package.id).$Version.nupkg"
        $download = Join-Path $temporary "$($registry.name)-$id.nupkg"
        $url = $base[0].'@id'.TrimEnd('/') + "/$id/$lowerVersion/$id.$lowerVersion.nupkg"
        $response = Invoke-WebRequest -Uri $url -Headers $registry.headers -OutFile $download -PassThru -SkipHttpErrorCheck
        if ($response.StatusCode -eq 404) {
            $plan += @{ registry = $registry.name; package = $package.id; action = 'push'; candidateSha256 = (Get-FileHash $candidate -Algorithm SHA256).Hash }
            continue
        }
        if ($response.StatusCode -ne 200) { throw "Registry lookup failed ($($response.StatusCode)): $($registry.name)/$id." }
        $candidateHash = (Get-FileHash $candidate -Algorithm SHA256).Hash
        $remoteHash = (Get-FileHash $download -Algorithm SHA256).Hash
        if ($candidateHash -cne $remoteHash) {
            if ($registry.name -cne 'nuget') { throw "Existing registry bytes differ: $($registry.name)/$id." }
            # nuget.org adds a repository signature. Require a valid signature and identical payload bytes.
            & dotnet nuget verify $download --all
            if ($LASTEXITCODE -ne 0) { throw "Existing package signature is invalid: $id." }
            $expected = Get-Payload $candidate
            $actual = Get-Payload $download
            if ($expected.Count -ne $actual.Count) { throw "Existing package inventory differs: $id." }
            foreach ($name in $expected.Keys) {
                if (-not $actual.ContainsKey($name) -or $actual[$name] -cne $expected[$name]) { throw "Existing package payload differs: $id/$name." }
            }
        }
        $plan += @{ registry = $registry.name; package = $package.id; action = 'verified-existing'; candidateSha256 = $candidateHash; registrySha256 = $remoteHash }
    }
}
if ($RequireEmptyRegistries -and @($plan | Where-Object action -EQ 'verified-existing').Count -ne 0) {
    throw 'Published packages exist, but the original candidate artifact is unavailable. Refusing to repack a retry.'
}
@{ version = $Version; entries = $plan } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host 'Both registries were checked before publication.'

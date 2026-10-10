[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BundlePath,
    [Parameter(Mandatory)] [string] $ResultsPath,
    [Parameter(Mandatory)] [string] $Commit,
    [string] $PackageVersion,
    [string] $RepositoryBranch,
    [string] $RuntimeIdentifier = 'linux-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$bundle = (Resolve-Path -LiteralPath $BundlePath).Path
$manifest = Get-Content -Raw (Join-Path $repoRoot 'eng/release-manifest.json') | ConvertFrom-Json
if (-not $PackageVersion) { $PackageVersion = $manifest.packageVersion }
if (-not $RepositoryBranch) { $RepositoryBranch = $manifest.assets.repositoryBranch }
$results = [System.IO.Path]::GetFullPath($ResultsPath)
if (Test-Path $results) { throw 'ResultsPath must not exist; each run needs fresh evidence.' }
New-Item -ItemType Directory -Path $results | Out-Null
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('inquiry-package-consumers-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workRoot | Out-Null
$previousPackages = $env:NUGET_PACKAGES
$previousHttpCache = $env:NUGET_HTTP_CACHE_PATH
$env:NUGET_PACKAGES = Join-Path $workRoot 'packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $workRoot 'http-cache'
$checks = [System.Collections.Generic.List[string]]::new()

function Invoke-Dotnet([string[]] $Arguments) {
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $results 'consumers.log') -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}

function New-Consumer([string] $Name, [string] $Tfm, [string[]] $Packages, [string[]] $Sources, [switch] $AspNetCore) {
    $directory = Join-Path $workRoot $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    foreach ($source in $Sources) { Copy-Item -LiteralPath $source -Destination $directory }
    $references = foreach ($id in $Packages) {
        '<PackageReference Include="' + $id + '" Version="' + $PackageVersion + '" />'
    }
    $diVersion = $manifest.packages[0].dependencies.'Microsoft.Extensions.DependencyInjection.Abstractions'
    if (-not $AspNetCore) {
        $references += '<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="' + $diVersion + '" />'
    }
    if ($AspNetCore) { $references += '<FrameworkReference Include="Microsoft.AspNetCore.App" />' }
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$Tfm</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>$($references -join "`n")</ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $directory 'Consumer.csproj') -Encoding utf8
    return (Join-Path $directory 'Consumer.csproj')
}

try {
    Invoke-Dotnet @('run', '--project', (Join-Path $repoRoot 'eng/Inquiry.ReleaseTools'), '-c', 'Release', '--',
        'verify-bundle', $repoRoot, (Join-Path $repoRoot 'eng/release-manifest.json'), $bundle, $Commit, '--version', $PackageVersion, '--branch', $RepositoryBranch)
    $hashes = @(Get-ChildItem -LiteralPath $bundle -File | Sort-Object Name | ForEach-Object {
        @{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    # Exact IDs take precedence over the public wildcard, so no Inquiry package can fall back to nuget.org.
    $mapping = ($manifest.packages | ForEach-Object { '<package pattern="' + $_.id + '" />' }) -join "`n"
    $escapedFeed = [System.Security.SecurityElement]::Escape($bundle)
    $config = Join-Path $workRoot 'NuGet.config'
    @"
<configuration>
  <packageSources><clear /><add key="candidate" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="candidate">$mapping</packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8
    $sample = Join-Path $repoRoot 'samples/Inquiry.AotSmoke'
    $providers = @{ Sqlite = 'sqlite'; SqlServer = 'sqlserver'; PostgreSql = 'postgresql'; MySql = 'mysql'; MariaDb = 'mariadb'; Oracle = 'oracle' }
    foreach ($tfm in @('net8.0', 'net9.0', 'net10.0')) {
        foreach ($provider in ($providers.Keys | Sort-Object)) {
            $project = New-Consumer "provider-$provider-$tfm" $tfm @("Ignyte.Inquiry.$provider") @((Join-Path $PSScriptRoot 'Program.cs'), (Join-Path $sample 'Widget.cs'))
            Invoke-Dotnet @('restore', $project, '--configfile', $config, '--no-cache')
            Invoke-Dotnet @('run', '--project', $project, '-c', 'Release', '--no-restore', '--', $providers[$provider])
            $checks.Add("$tfm/$provider/analyzer-and-generated-crud-build")
        }
        $project = New-Consumer "crud-$tfm" $tfm @('Ignyte.Inquiry.Sqlite') @((Join-Path $sample 'Program.cs'), (Join-Path $sample 'Widget.cs'))
        Invoke-Dotnet @('restore', $project, '--configfile', $config, '--no-cache')
        Invoke-Dotnet @('run', '--project', $project, '-c', 'Release', '--no-restore')
        $checks.Add("$tfm/sqlite/crud")
        $project = New-Consumer "companions-$tfm" $tfm @('Ignyte.Inquiry.Aspire', 'Ignyte.Inquiry.AspNetCore', 'Ignyte.Inquiry.Interceptors', 'Ignyte.Inquiry.Testing', 'Ignyte.Inquiry.Sqlite.Testing', 'Ignyte.Inquiry.Sqlite') @((Join-Path $PSScriptRoot 'Companions.cs')) -AspNetCore
        Invoke-Dotnet @('restore', $project, '--configfile', $config, '--no-cache')
        Invoke-Dotnet @('run', '--project', $project, '-c', 'Release', '--no-restore')
        $checks.Add("$tfm/companions")
    }
    $project = New-Consumer 'native' 'net10.0' @('Ignyte.Inquiry.Sqlite') @((Join-Path $sample 'Program.cs'), (Join-Path $sample 'Widget.cs'))
    $nativeOutput = Join-Path $workRoot 'native-out'
    Invoke-Dotnet @('publish', $project, '-c', 'Release', '-r', $RuntimeIdentifier, '-p:PublishAot=true', "-p:RestoreConfigFile=$config", '-o', $nativeOutput)
    $binary = Join-Path $nativeOutput $(if ($IsWindows) { 'Consumer.exe' } else { 'Consumer' })
    & $binary 2>&1 | Tee-Object -FilePath (Join-Path $results 'consumers.log') -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The actual-package NativeAOT consumer failed.' }
    $checks.Add("net10.0/$RuntimeIdentifier/nativeaot-crud")
    foreach ($file in $hashes) {
        if ((Get-FileHash -LiteralPath (Join-Path $bundle $file.file) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) {
            throw "Bundle changed during verification: $($file.file)"
        }
    }
    @{ schemaVersion = 'inquiry-package-consumers-v1'; commit = $Commit; packageVersion = $PackageVersion; repositoryBranch = $RepositoryBranch; packages = $hashes; checks = $checks.ToArray(); status = 'passed' } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $results 'package-consumers.json') -Encoding utf8
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
    $env:NUGET_HTTP_CACHE_PATH = $previousHttpCache
    # Keep failed consumer projects for diagnosis; they contain no credentials.
    Write-Host "Consumer workspace: $workRoot"
}

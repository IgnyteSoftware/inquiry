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
$checks = [System.Collections.Generic.List[string]]::new()

function Invoke-Dotnet([string[]] $Arguments) {
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $results 'consumers.log') -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}

# A package entry is an ID, or ID=Version to reference a version other than the candidate's.
function New-Consumer([string] $Name, [string] $Tfm, [string[]] $Packages, [string[]] $Sources, [switch] $AspNetCore, [switch] $Logging, [bool] $WarningsAsErrors = $true) {
    $directory = Join-Path $workRoot $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    foreach ($source in $Sources) { Copy-Item -LiteralPath $source -Destination $directory }
    $references = foreach ($package in $Packages) {
        $id, $version = $package -split '=', 2
        '<PackageReference Include="' + $id + '" Version="' + $(if ($version) { $version } else { $PackageVersion }) + '" />'
    }
    $core = $manifest.packages | Where-Object id -CEQ 'Ignyte.Inquiry'
    $diVersion = $core.dependencies.'Microsoft.Extensions.DependencyInjection.Abstractions'
    if (-not $AspNetCore) {
        $references += '<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="' + $diVersion + '" />'
    }
    if ($AspNetCore) { $references += '<FrameworkReference Include="Microsoft.AspNetCore.App" />' }
    if ($Logging) {
        $loggingVersion = $core.dependencies.'Microsoft.Extensions.Logging.Abstractions'
        $references += '<PackageReference Include="Microsoft.Extensions.Logging" Version="' + $loggingVersion + '" />'
    }
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$Tfm</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>$($WarningsAsErrors.ToString().ToLowerInvariant())</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>$($references -join "`n")</ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $directory 'Consumer.csproj') -Encoding utf8
    return (Join-Path $directory 'Consumer.csproj')
}

# Recipe files come from marked fences in the published pages, so the docs cannot drift from what runs here.
function Copy-Recipe([string] $Recipe, [string] $Directory) {
    $pattern = '(?ms)^<!-- recipe: ' + [regex]::Escape($Recipe) + '/(?<file>[A-Za-z.]+) -->\r?\n```[a-z]*\r?\n(?<code>.*?)^```'
    $files = foreach ($page in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'docs/site/articles') -Recurse -Filter '*.md') {
        foreach ($match in [regex]::Matches((Get-Content -Raw -LiteralPath $page.FullName), $pattern)) {
            Set-Content -LiteralPath (Join-Path $Directory $match.Groups['file'].Value) -Value $match.Groups['code'].Value -NoNewline -Encoding utf8
            $match.Groups['file'].Value
        }
    }
    return @($files)
}

function Invoke-Recipe([string] $Recipe, [string[]] $ExpectedFiles, [string[]] $Sources, [string[]] $Packages = @('Ignyte.Inquiry.Sqlite'), [switch] $Logging) {
    $project = New-Consumer "recipe-$Recipe" 'net10.0' $Packages $Sources -Logging:$Logging
    $directory = Split-Path -Parent $project
    $files = Copy-Recipe $Recipe $directory
    if (@(Compare-Object ($files | Sort-Object) ($ExpectedFiles | Sort-Object)).Count -ne 0) {
        throw "Documentation recipe '$Recipe' extracted [$($files -join ', ')], expected [$($ExpectedFiles -join ', ')]."
    }
    Invoke-Dotnet @('restore', $project, '--configfile', $config, '--no-cache')
    Invoke-Dotnet @('build', $project, '-c', 'Release', '--no-restore')
    # Recipes create their database file in the working directory, which must be new.
    Push-Location -LiteralPath $directory
    try {
        $output = @(& dotnet run --project $project -c Release --no-build 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $output | Tee-Object -FilePath (Join-Path $results 'consumers.log') -Append | Out-Host
    if ($exitCode -ne 0) { throw "Documentation recipe '$Recipe' failed ($exitCode)." }
    return $output
}

# Restore reads only the nuspec, so a candidate copy with a rewritten version stands in for another release.
function New-ProbePackage([string] $Id, [string] $Version) {
    $path = Join-Path $probeFeed "$Id.$Version.nupkg"
    Copy-Item -LiteralPath (Join-Path $bundle "$Id.$PackageVersion.nupkg") -Destination $path
    $archive = [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.GetEntry("$Id.nuspec")
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $updated = $nuspec.Replace("<version>$PackageVersion</version>", "<version>$Version</version>").Replace("version=`"[$PackageVersion]`"", "version=`"[$Version]`"")
        if (-not $updated.Contains("<version>$Version</version>")) { throw "Could not set the probe version of $Id to $Version." }
        $entry.Delete()
        $writer = [System.IO.StreamWriter]::new($archive.CreateEntry("$Id.nuspec").Open(), [System.Text.UTF8Encoding]::new($false))
        try { $writer.Write($updated) } finally { $writer.Dispose() }
    }
    finally {
        $archive.Dispose()
    }
}

function Invoke-RestoreProbe([string] $Name, [string[]] $Packages, [bool] $WarningsAsErrors, [bool] $ShouldFail, [string] $Pattern) {
    $project = New-Consumer "probe-$Name" 'net10.0' $Packages @() -WarningsAsErrors $WarningsAsErrors
    $output = @(& dotnet restore $project --configfile $probeConfig --no-cache 2>&1 | ForEach-Object { "$_" })
    $exitCode = $LASTEXITCODE
    $output | Tee-Object -FilePath (Join-Path $results 'consumers.log') -Append | Out-Host
    if (($exitCode -ne 0) -ne $ShouldFail) { throw "Restore probe '$Name' exited with $exitCode." }
    if ($Pattern -and -not ($output -match $Pattern)) { throw "Restore probe '$Name' did not report /$Pattern/." }
    if (-not $Pattern -and ($output -match '\bNU1\d{3}\b')) { throw "Restore probe '$Name' reported a NuGet diagnostic." }
    $checks.Add("net10.0/version-mismatch/$Name")
}

# The schema evolution sample has two entity libraries and runs against SQL Server, so it cannot use the
# single-project consumer. Its sources and Runner project are copied unchanged; only the library projects
# are generated, so they reference the candidate package instead of the repository's source projects.
function Invoke-SchemaEvolutionSample {
    $sample = Join-Path $repoRoot 'samples/Inquiry.SchemaEvolution'
    $directory = Join-Path $workRoot 'schema-evolution'
    foreach ($part in @('V1', 'V2', 'Runner', 'Scripts')) { New-Item -ItemType Directory -Path (Join-Path $directory $part) | Out-Null }
    Copy-Item -Path (Join-Path $sample '*.cs'), (Join-Path $sample 'expected-output.txt') -Destination $directory
    Copy-Item -Path (Join-Path $sample 'Scripts/*.sql') -Destination (Join-Path $directory 'Scripts')
    Copy-Item -Path (Join-Path $sample 'Runner/Program.cs'), (Join-Path $sample 'Runner/Inquiry.SchemaEvolution.csproj') -Destination (Join-Path $directory 'Runner')
    # SqlClient cannot open connections in invariant globalization mode, so this graph leaves it off.
    @"
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $directory 'Directory.Build.props') -Encoding utf8
    # Third-party versions and the transitive security pins come from the repository's central file.
    $centralPackages = [System.Security.SecurityElement]::Escape((Join-Path $repoRoot 'Directory.Packages.props'))
    @"
<Project>
  <Import Project="$centralPackages" />
  <ItemGroup><PackageVersion Include="Ignyte.Inquiry.SqlServer" Version="$PackageVersion" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $directory 'Directory.Packages.props') -Encoding utf8
    foreach ($version in @('V1', 'V2')) {
        Copy-Item -LiteralPath (Join-Path $sample "$version/Catalog.cs") -Destination (Join-Path $directory $version)
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Ignyte.Inquiry.SqlServer" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $directory "$version/Inquiry.SchemaEvolution.$version.csproj") -Encoding utf8
    }
    $project = Join-Path $directory 'Runner/Inquiry.SchemaEvolution.csproj'
    Invoke-Dotnet @('restore', $project, '--configfile', $config, '--no-cache')
    foreach ($version in @('V1', 'V2')) {
        $assets = Get-Content -Raw -LiteralPath (Join-Path $directory "$version/obj/project.assets.json") | ConvertFrom-Json
        if (-not $assets.libraries.PSObject.Properties["Ignyte.Inquiry.SqlServer/$PackageVersion"]) {
            throw "The schema evolution $version library did not restore the candidate Ignyte.Inquiry.SqlServer $PackageVersion."
        }
    }
    Invoke-Dotnet @('build', $project, '-c', 'Release', '--no-restore')
    # The Runner starts its own SQL Server container and has no skip path: without Docker it exits nonzero.
    # It disposes the container itself, so the resource reaper and its Docker Hub pull are not needed.
    $errorLog = Join-Path $results 'schema-evolution.stderr.log'
    $previousRyuk = $env:TESTCONTAINERS_RYUK_DISABLED
    $env:TESTCONTAINERS_RYUK_DISABLED = 'true'
    try {
        $output = @(& dotnet run --project $project -c Release --no-build 2> $errorLog | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $env:TESTCONTAINERS_RYUK_DISABLED = $previousRyuk
    }
    $output | Tee-Object -FilePath (Join-Path $results 'consumers.log') -Append | Out-Host
    if ($exitCode -ne 0) { throw "The schema evolution sample failed ($exitCode)." }
    $expected = @(Get-Content -LiteralPath (Join-Path $directory 'expected-output.txt'))
    if (($output -join "`n") -cne ($expected -join "`n")) { throw 'The schema evolution output differs from its documented output.' }
}

try {
    Invoke-Dotnet @('run', '--project', (Join-Path $repoRoot 'eng/Inquiry.ReleaseTools'), '-c', 'Release', '--',
        'verify-bundle', $repoRoot, (Join-Path $repoRoot 'eng/release-manifest.json'), $bundle, $Commit, '--version', $PackageVersion, '--branch', $RepositoryBranch)
    $env:NUGET_PACKAGES = Join-Path $workRoot 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $workRoot 'http-cache'
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
    $output = @(Invoke-Recipe 'getting-started' @('Shipper.cs', 'ShipperStore.cs', 'Program.cs', 'output.txt') @())
    $expected = @(Get-Content -LiteralPath (Join-Path $workRoot 'recipe-getting-started/output.txt'))
    if (($output -join "`n") -cne ($expected -join "`n")) { throw 'The getting-started output differs from its documented output.' }
    $checks.Add('net10.0/sqlite/docs-recipe-getting-started')
    $output = @(Invoke-Recipe 'partial-updates' @('schema.sql', 'ContactPatches.cs') @((Join-Path $PSScriptRoot 'PartialUpdatesRecipe.cs')))
    if ($output[-1] -cne 'PARTIAL-UPDATES-RECIPE-OK') { throw 'The partial-updates recipe did not complete.' }
    $checks.Add('net10.0/sqlite/docs-recipe-partial-updates')
    $output = @(Invoke-Recipe 'entity-mapping' @('Models.cs', 'Stores.cs', 'SqlTrace.cs', 'Program.cs', 'output.txt') @())
    $expected = @(Get-Content -LiteralPath (Join-Path $workRoot 'recipe-entity-mapping/output.txt'))
    if (($output -join "`n") -cne ($expected -join "`n")) { throw 'The entity-mapping output differs from its documented output.' }
    $checks.Add('net10.0/sqlite/docs-recipe-entity-mapping')
    $output = @(Invoke-Recipe 'aggregates' @('Product.cs', 'ProductStore.cs', 'Program.cs', 'output.txt') @())
    $expected = @(Get-Content -LiteralPath (Join-Path $workRoot 'recipe-aggregates/output.txt'))
    if (($output -join "`n") -cne ($expected -join "`n")) { throw 'The aggregates output differs from its documented output.' }
    $checks.Add('net10.0/sqlite/docs-recipe-aggregates')
    $output = @(Invoke-Recipe 'n-plus-one' @('Order.cs', 'OrderStore.cs', 'NPlusOneExample.cs') @((Join-Path $PSScriptRoot 'NPlusOneRecipe.cs')) @('Ignyte.Inquiry.Sqlite', 'Ignyte.Inquiry.Interceptors') -Logging)
    if ($output[-1] -cne 'N-PLUS-ONE-RECIPE-OK') { throw 'The N+1 detection recipe did not complete.' }
    $checks.Add('net10.0/sqlite/docs-recipe-n-plus-one')
    $sample = Join-Path $repoRoot 'samples/Inquiry.AotSmoke'
    $providers = @{ Sqlite = 'sqlite'; SqlServer = 'sqlserver'; PostgreSql = 'postgresql'; MySql = 'mysql'; MariaDb = 'mariadb'; Oracle = 'oracle' }
    foreach ($tfm in @('net10.0', 'net11.0')) {
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
    # Family packages pin each other exactly (#273), so NuGet must reject a mixed set at restore.
    $probeFeed = Join-Path $workRoot 'probe-feed'
    New-Item -ItemType Directory -Path $probeFeed | Out-Null
    $stable = [version]$manifest.packageVersion
    $older = '0.1.0'
    $newer = "$($stable.Major).$($stable.Minor).$($stable.Build + 1)"
    New-ProbePackage 'Ignyte.Inquiry' $older
    New-ProbePackage 'Ignyte.Inquiry' $newer
    New-ProbePackage 'Ignyte.Inquiry.Testing' $newer
    $escapedProbeFeed = [System.Security.SecurityElement]::Escape($probeFeed)
    $probeConfig = Join-Path $workRoot 'NuGet.probe.config'
    @"
<configuration>
  <packageSources><clear /><add key="candidate" value="$escapedFeed" /><add key="probe" value="$escapedProbeFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="candidate">$mapping</packageSource><packageSource key="probe">$mapping</packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $probeConfig -Encoding utf8
    $provider = 'Ignyte.Inquiry.Sqlite'
    $core = [regex]::Escape('Ignyte.Inquiry')
    Invoke-RestoreProbe 'companion-newer-nu1107' @($provider, "Ignyte.Inquiry.Testing=$newer") $false $true "error NU1107: .*$core"
    Invoke-RestoreProbe 'core-older-nu1605' @($provider, "Ignyte.Inquiry=$older") $false $true "error NU1605: .*$core"
    # A newer direct reference wins NuGet resolution and breaks the provider's range only as warning NU1608.
    Invoke-RestoreProbe 'core-newer-nu1608-warning' @($provider, "Ignyte.Inquiry=$newer") $false $false "warning NU1608: .*$core \(= $([regex]::Escape($PackageVersion))\)"
    Invoke-RestoreProbe 'core-newer-nu1608-warnings-as-errors' @($provider, "Ignyte.Inquiry=$newer") $true $true "error NU1608: .*$core"
    Invoke-RestoreProbe 'coordinated-restores' @($provider, 'Ignyte.Inquiry.Testing', 'Ignyte.Inquiry') $false $false $null
    $assets = Get-Content -Raw (Join-Path $workRoot 'probe-coordinated-restores/obj/project.assets.json') | ConvertFrom-Json
    if (-not $assets.libraries.PSObject.Properties["Ignyte.Inquiry/$PackageVersion"]) { throw 'The coordinated probe did not resolve the candidate Ignyte.Inquiry.' }

    $project = New-Consumer 'native' 'net10.0' @('Ignyte.Inquiry.Sqlite') @((Join-Path $sample 'Program.cs'), (Join-Path $sample 'Widget.cs'))
    $nativeOutput = Join-Path $workRoot 'native-out'
    Invoke-Dotnet @('publish', $project, '-c', 'Release', '-r', $RuntimeIdentifier, '-p:PublishAot=true', "-p:RestoreConfigFile=$config", '-o', $nativeOutput)
    $binary = Join-Path $nativeOutput $(if ($IsWindows) { 'Consumer.exe' } else { 'Consumer' })
    & $binary 2>&1 | Tee-Object -FilePath (Join-Path $results 'consumers.log') -Append | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The actual-package NativeAOT consumer failed.' }
    $checks.Add("net10.0/$RuntimeIdentifier/nativeaot-crud")
    Invoke-SchemaEvolutionSample
    $checks.Add('net10.0/sqlserver/docs-sample-schema-evolution')
    if (@(Compare-Object ($hashes.file | Sort-Object) ((Get-ChildItem -LiteralPath $bundle -File).Name | Sort-Object)).Count -ne 0) {
        throw 'Bundle inventory changed during verification.'
    }
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

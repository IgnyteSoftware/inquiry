[CmdletBinding()]
param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$version = "1.0.0-package-smoke"
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("inquiry-package-smoke-" + [Guid]::NewGuid().ToString("N"))
$feed = Join-Path $workRoot "feed"
$isolatedRepo = Join-Path $workRoot "repo"
$packages = Join-Path $workRoot "packages"
$nugetConfig = Join-Path $workRoot "NuGet.config"
$previousNodeReuse = $env:MSBUILDDISABLENODEREUSE
$previousBuildServer = $env:DOTNET_CLI_USE_MSBUILD_SERVER
$env:MSBUILDDISABLENODEREUSE = "1"
$env:DOTNET_CLI_USE_MSBUILD_SERVER = "0"

function Get-RelativePath([string] $basePath, [string] $path) {
    $baseUri = [Uri]((Resolve-Path -LiteralPath $basePath).Path.TrimEnd('\') + '\')
    $pathUri = [Uri](Resolve-Path -LiteralPath $path).Path
    [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Copy-ProjectTree([string] $relativePath) {
    $source = Join-Path $repoRoot $relativePath
    $destination = Join-Path $isolatedRepo $relativePath

    Get-ChildItem -LiteralPath $source -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' } |
        ForEach-Object {
            $relativeFile = Get-RelativePath $source $_.FullName
            $destinationFile = Join-Path $destination $relativeFile
            New-Item -ItemType Directory -Path (Split-Path $destinationFile) -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destinationFile
        }
}

function Get-RepositoryBuildState {
    $projectRoots = @('src', 'tests', 'samples', 'benchmarks') |
        ForEach-Object { Join-Path $repoRoot $_ } |
        Where-Object { Test-Path -LiteralPath $_ }
    $buildFiles = Get-ChildItem -LiteralPath $projectRoots -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' } |
        ForEach-Object {
            $projectDirectory = $_.DirectoryName
            foreach ($outputDirectory in @('bin', 'obj')) {
                $path = Join-Path $projectDirectory $outputDirectory
                if (Test-Path -LiteralPath $path) {
                    Get-ChildItem -LiteralPath $path -Recurse -File
                }
            }
        }

    $records = $buildFiles |
        Sort-Object FullName -Unique |
        ForEach-Object {
            $relativeFile = Get-RelativePath $repoRoot $_.FullName
            if ($_.Name -eq 'project.assets.json') {
                $contentState = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
            else {
                $contentState = $_.LastWriteTimeUtc.Ticks
            }
            "$relativeFile|$($_.Length)|$contentState"
        }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($records -join "`n"))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        ([BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

try {
    $providers = @(
        @{ Package = 'Inquiry.Sqlite'; ProviderId = 'sqlite' },
        @{ Package = 'Inquiry.SqlServer'; ProviderId = 'sqlserver' },
        @{ Package = 'Inquiry.PostgreSql'; ProviderId = 'postgresql' },
        @{ Package = 'Inquiry.MySql'; ProviderId = 'mysql' },
        @{ Package = 'Inquiry.MariaDb'; ProviderId = 'mariadb' },
        @{ Package = 'Inquiry.Oracle'; ProviderId = 'oracle' }
    )
    $repositoryBuildStateBefore = Get-RepositoryBuildState
    New-Item -ItemType Directory -Path $feed, $packages, $isolatedRepo | Out-Null

    foreach ($file in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $isolatedRepo
    }
    $projectTrees = @('src\Inquiry', 'src\Inquiry.Generators.Shared')
    foreach ($provider in $providers) {
        $projectTrees += "src\$($provider.Package)"
        $projectTrees += "src\$($provider.Package).Analyzer"
    }
    foreach ($projectTree in $projectTrees) {
        Copy-ProjectTree $projectTree
    }

    $escapedFeed = [System.Security.SecurityElement]::Escape($feed)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="package-smoke" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <config>
    <add key="globalPackagesFolder" value="packages" />
  </config>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfig -Encoding utf8

    $inquiryProject = Join-Path $isolatedRepo "src\Inquiry\Inquiry.csproj"
    foreach ($provider in $providers) {
        $providerProject = Join-Path $isolatedRepo "src\$($provider.Package)\$($provider.Package).csproj"
        dotnet restore $providerProject --configfile $nugetConfig --no-cache -p:MinVerVersionOverride=$version
        if ($LASTEXITCODE -ne 0) { throw "Restoring $($provider.Package) with the isolated NuGet configuration failed." }
    }

    dotnet pack $inquiryProject -c $Configuration -o $feed --no-restore -p:MinVerVersionOverride=$version
    if ($LASTEXITCODE -ne 0) { throw "Packing Inquiry failed." }

    foreach ($provider in $providers) {
        $providerProject = Join-Path $isolatedRepo "src\$($provider.Package)\$($provider.Package).csproj"
        dotnet pack $providerProject -c $Configuration -o $feed --no-restore -p:MinVerVersionOverride=$version
        if ($LASTEXITCODE -ne 0) { throw "Packing $($provider.Package) failed." }

        $consumer = Join-Path $workRoot "consumer-$($provider.ProviderId)"
        New-Item -ItemType Directory -Path $consumer | Out-Null
        Copy-Item (Join-Path $PSScriptRoot "Inquiry.PackageSmoke.csproj") $consumer
        Copy-Item (Join-Path $PSScriptRoot "Program.cs") $consumer
        $project = Join-Path $consumer "Inquiry.PackageSmoke.csproj"
        $properties = @("-p:InquirySmokeVersion=$version", "-p:InquirySmokeProviderPackage=$($provider.Package)")

        dotnet restore $project --configfile $nugetConfig --no-cache @properties
        if ($LASTEXITCODE -ne 0) { throw "Restoring the isolated $($provider.Package) consumer failed." }

        dotnet build $project -c $Configuration --no-restore @properties
        if ($LASTEXITCODE -ne 0) { throw "Building the isolated $($provider.Package) consumer failed." }

        dotnet run --project $project -c $Configuration --no-build @properties -- $provider.ProviderId
        if ($LASTEXITCODE -ne 0) { throw "Packed $($provider.Package) manifest verification failed." }
    }

    $repositoryBuildStateAfter = Get-RepositoryBuildState
    if ($repositoryBuildStateAfter -ne $repositoryBuildStateBefore) {
        throw "The isolated package smoke changed repository bin/obj output or a project.assets.json file."
    }

    dotnet build (Join-Path $repoRoot "Inquiry.slnx") -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "The repository no-restore build failed after the isolated package smoke." }
}
finally {
    for ($attempt = 1; $attempt -le 10 -and (Test-Path -LiteralPath $workRoot); $attempt++) {
        try {
            Remove-Item -LiteralPath $workRoot -Recurse -Force
        }
        catch {
            if ($attempt -eq 10) { throw }
            Start-Sleep -Milliseconds 200
        }
    }
    $env:MSBUILDDISABLENODEREUSE = $previousNodeReuse
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = $previousBuildServer
}

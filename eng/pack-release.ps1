[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [string] $Commit,

    [string] $Tag,

    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$Commit = if ([string]::IsNullOrWhiteSpace($Commit)) {
    (& git -C $repositoryRoot rev-parse HEAD).Trim()
}
else {
    $Commit
}
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}

$rootPrefix = [System.IO.Path]::GetFullPath($repositoryRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($resolvedOutput.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must be outside the repository and immutable packing snapshot.'
}

if ($Commit -cnotmatch '^[0-9a-f]{40}$' -and $Commit -cnotmatch '^[0-9a-f]{64}$') {
    throw 'Commit must be a complete lowercase 40- or 64-character hexadecimal object ID.'
}

$headCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $headCommit -cne $Commit) {
    throw "Commit '$Commit' is not the checked-out HEAD '$headCommit'."
}

$worktreeStatus = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect the release worktree.'
}
if ($worktreeStatus.Count -ne 0) {
    throw 'Release packing requires a clean worktree, including no untracked files.'
}

if ($NoBuild) {
    throw '-NoBuild is incompatible with immutable detached-snapshot packing.'
}

if (Test-Path $resolvedOutput) {
    if ((Get-Item -Force $resolvedOutput).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw "Output directory cannot be a reparse point or symbolic link: $resolvedOutput"
    }
    if (Get-ChildItem -Force $resolvedOutput | Select-Object -First 1) {
        throw "Output directory must be empty: $resolvedOutput"
    }
}
else {
    New-Item -ItemType Directory -Path $resolvedOutput | Out-Null
}

if ((Get-Item -Force $resolvedOutput).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
    throw "Output directory cannot be a reparse point or symbolic link: $resolvedOutput"
}

$snapshotParent = Join-Path ([System.IO.Path]::GetTempPath()) ("inquiry-release-snapshot-" + [guid]::NewGuid().ToString('N'))
$snapshotRoot = Join-Path $snapshotParent 'source'
$snapshotRegistered = $false
New-Item -ItemType Directory -Path $snapshotParent | Out-Null

try {
    & git -C $repositoryRoot worktree add --detach $snapshotRoot $Commit
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create an immutable detached packing snapshot for '$Commit'."
    }
    $snapshotRegistered = $true
    if ((Get-Item -Force $snapshotRoot).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw 'Detached packing snapshot root cannot be a reparse point or symbolic link.'
    }

    $snapshotHead = (& git -C $snapshotRoot rev-parse HEAD).Trim()
    $snapshotStatus = @(& git -C $snapshotRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $snapshotHead -cne $Commit -or $snapshotStatus.Count -ne 0) {
        throw 'Detached packing snapshot does not exactly represent the requested clean commit.'
    }

    $manifestPath = Join-Path $snapshotRoot 'eng/release-manifest.json'
    $toolProject = Join-Path $snapshotRoot 'eng/Inquiry.ReleaseTools/Inquiry.ReleaseTools.csproj'
    $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
    if ($Tag -and $Tag -cne $manifest.tag) {
        throw "Tag '$Tag' does not equal manifest tag '$($manifest.tag)'."
    }

    & dotnet restore (Join-Path $snapshotRoot 'Inquiry.slnx')
    if ($LASTEXITCODE -ne 0) {
        throw 'Detached release snapshot restore failed.'
    }

    & dotnet run --project $toolProject --configuration Release -- verify-manifest $snapshotRoot $manifestPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Release manifest verification failed.'
    }

    foreach ($package in $manifest.packages) {
        $project = Join-Path $snapshotRoot $package.project
        $arguments = @(
            'pack', $project,
            '--configuration', 'Release',
            '--output', $resolvedOutput,
            '--no-restore',
            '-p:ContinuousIntegrationBuild=true',
            "-p:MinVerVersionOverride=$($manifest.packageVersion)",
            "-p:RepositoryCommit=$Commit",
            "-p:RepositoryBranch=$($manifest.assets.repositoryBranch)"
        )

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Packing $($package.id) failed."
        }
    }

    $verifyArguments = @(
        'run', '--project', $toolProject,
        '--configuration', 'Release', '--',
        'verify-bundle', $snapshotRoot, $manifestPath, $resolvedOutput, $Commit
    )
    if ($Tag) {
        $verifyArguments += $Tag
    }

    & dotnet @verifyArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Release bundle verification failed.'
    }
}
finally {
    if ($snapshotRegistered) {
        & git -C $repositoryRoot worktree remove --force $snapshotRoot
    }
    if (Test-Path $snapshotParent) {
        Remove-Item -LiteralPath $snapshotParent -Recurse -Force
    }
}

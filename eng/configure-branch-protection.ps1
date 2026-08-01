<#
.SYNOPSIS
    Configures branch protection rules for the main branch.

.DESCRIPTION
    Requires GitHub Pro or a public repository. Run once after making the
    repository public or upgrading to Pro. Uses the GitHub CLI (gh).

.EXAMPLE
    ./eng/configure-branch-protection.ps1
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$owner = 'IgnyteSoftware'
$repo  = 'inquiry'

$body = @{
    required_status_checks = @{
        strict = $true
        checks = @(
            @{ context = 'ci-required-v1'; app_id = 15368 }
        )
    }
    enforce_admins = $false
    required_pull_request_reviews = @{
        required_approving_review_count = 1
        dismiss_stale_reviews           = $true
    }
    restrictions                    = $null
    required_linear_history         = $false
    allow_force_pushes              = $false
    allow_deletions                 = $false
    required_conversation_resolution = $true
} | ConvertTo-Json -Depth 4

$body | gh api --method PUT "repos/$owner/$repo/branches/main/protection" --input -

if ($LASTEXITCODE -ne 0) {
    throw 'Failed to configure branch protection. Ensure the repo is public or on GitHub Pro.'
}

Write-Host 'Branch protection configured:'
Write-Host '  - Required status check: ci-required-v1 (strict, pinned to GitHub Actions)'
Write-Host '  - Required review: 1 approval, dismiss stale'
Write-Host '  - Force push: disabled (admin bypass enabled)'
Write-Host '  - Deletion: disabled'
Write-Host '  - Conversation resolution: required'

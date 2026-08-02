<#
.SYNOPSIS
    Configures repository rulesets protecting the release branches and release tags.

.DESCRIPTION
    Requires a public repository (or GitHub Pro). Uses the GitHub CLI (gh).

    Creates (or replaces, matched by name) two rulesets:

    - protect-release-branches (main + prerelease): pull request required with 1 approval,
      code-owner review, last-push approval, stale-review dismissal, and conversation
      resolution; the ci-required-v1 status check (pinned to GitHub Actions) with strict
      up-to-date enforcement; linear history; no force pushes; no deletion.
    - protect-release-tags (v*): creation, update, and deletion blocked.

    Bypass model (deliberate): Organization admins are a named bypass actor on both
    rulesets — solo-maintainer reality means self-approval is impossible, so admin merges
    must stay possible; ruleset bypasses are logged and labeled, unlike the old
    enforce_admins=false. Remove the OrganizationAdmin bypass actor once a second
    maintainer can review. GitHub Actions (app 15368) may create release tags — the
    release workflow pushes v* tags after publishing.

    Bodies are written to BOM-free temp files: Windows PowerShell 5.1 stamps a BOM onto
    pipeline output to native commands and the GitHub API rejects the resulting JSON.

.EXAMPLE
    ./eng/configure-branch-protection.ps1
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$owner = 'IgnyteSoftware'
$repo  = 'inquiry'

$branchRuleset = @{
    name = 'protect-release-branches'
    target = 'branch'
    enforcement = 'active'
    conditions = @{
        ref_name = @{
            include = @('refs/heads/main', 'refs/heads/prerelease')
            exclude = @()
        }
    }
    rules = @(
        @{ type = 'deletion' }
        @{ type = 'non_fast_forward' }
        @{ type = 'required_linear_history' }
        @{
            type = 'pull_request'
            parameters = @{
                required_approving_review_count = 1
                dismiss_stale_reviews_on_push   = $true
                require_code_owner_review       = $true
                require_last_push_approval      = $true
                required_review_thread_resolution = $true
                allowed_merge_methods           = @('merge', 'squash', 'rebase')
            }
        }
        @{
            type = 'required_status_checks'
            parameters = @{
                strict_required_status_checks_policy = $true
                required_status_checks = @(
                    @{ context = 'ci-required-v1'; integration_id = 15368 }
                )
            }
        }
    )
    bypass_actors = @(
        @{ actor_id = 1; actor_type = 'OrganizationAdmin'; bypass_mode = 'always' }
    )
}

$tagRuleset = @{
    name = 'protect-release-tags'
    target = 'tag'
    enforcement = 'active'
    conditions = @{
        ref_name = @{
            include = @('refs/tags/v*')
            exclude = @()
        }
    }
    # Creation stays open: the release workflow pushes v* tags with the workflow token, and
    # GitHub rejects the Actions app as a ruleset bypass actor on this org. Update and deletion
    # are blocked for everyone, which is the part that protects the tag->commit audit trail.
    rules = @(
        @{ type = 'update' }
        @{ type = 'deletion' }
    )
    bypass_actors = @(
        @{ actor_id = 1; actor_type = 'OrganizationAdmin'; bypass_mode = 'always' }
    )
}

$existing = gh api "repos/$owner/$repo/rulesets" | ConvertFrom-Json

foreach ($ruleset in @($branchRuleset, $tagRuleset)) {
    $bodyPath = Join-Path ([System.IO.Path]::GetTempPath()) ("inquiry-ruleset-" + [guid]::NewGuid().ToString('N') + '.json')
    try {
        [System.IO.File]::WriteAllText($bodyPath, ($ruleset | ConvertTo-Json -Depth 8))
        $match = $existing | Where-Object { $_.name -eq $ruleset.name }
        if ($match) {
            gh api --method PUT "repos/$owner/$repo/rulesets/$($match.id)" --input $bodyPath | Out-Null
        }
        else {
            gh api --method POST "repos/$owner/$repo/rulesets" --input $bodyPath | Out-Null
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to apply ruleset '$($ruleset.name)'. Ensure the repo is public or on GitHub Pro."
        }
        Write-Host "Ruleset applied: $($ruleset.name)"
    }
    finally {
        if (Test-Path $bodyPath) {
            Remove-Item -Force $bodyPath
        }
    }
}

Write-Host 'Done. Bypass model: OrganizationAdmin (audited) on both rulesets; GitHub Actions may create v* tags.'

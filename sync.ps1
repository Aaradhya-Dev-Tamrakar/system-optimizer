<#
.SYNOPSIS
Safely sync the NovaOptimizer repository, verify build, detect secrets, and commit with smart conventional messaging.

.DESCRIPTION
Automated synchronization workflow for system-optimizer (NovaOptimizer):
1. Verifies Git repository and active branch.
2. Pulls remote updates with --rebase and --autostash if remote origin exists.
3. Optionally verifies project build via dotnet build --nologo.
4. Stages changes with git add -A.
5. Scans staged changes for accidental credentials, keys, or sensitive tokens.
6. Auto-generates conventional commit messages (feat/fix/refactor/docs/chore) with component scope and churn metrics.
7. Commits and safely pushes to origin, retrying with rebase if rejected.

.PARAMETER Message
Custom commit message (e.g. -m "feat(services): optimize background service tuning").
If omitted, an intelligent conventional commit message is auto-generated.

.PARAMETER PullOnly
Safely pull remote changes with --rebase --autostash without committing or pushing.

.PARAMETER SkipBuild
Bypasses dotnet build verification gate.

.PARAMETER NoPush
Stages and commits changes locally without pushing to remote origin.

.PARAMETER WhatIf
Dry-run mode: Previews changes, build status, and auto-generated commit message without modifying git state.

.EXAMPLE
.\sync.ps1
.\sync.ps1 -m "feat(ui): add system monitor glass card"
.\sync.ps1 -PullOnly
.\sync.ps1 -WhatIf
#>

[CmdletBinding()]
param (
    [Alias("m")]
    [string]$Message,

    [switch]$PullOnly,

    [switch]$SkipBuild,

    [switch]$NoPush,

    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Status {
    param(
        [string]$Message,
        [System.ConsoleColor]$Color = [System.ConsoleColor]::Cyan
    )
    Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] $Message" -ForegroundColor $Color
}

function Write-Notice {
    param([string]$Message)
    Write-Status -Message $Message -Color ([System.ConsoleColor]::Yellow)
}

function Write-Success {
    param([string]$Message)
    Write-Status -Message $Message -Color ([System.ConsoleColor]::Green)
}

function Find-StagedSecrets {
    $stagedDiff = git diff --cached -U0 2>$null
    if (-not $stagedDiff) { return @() }

    $addedLines = $stagedDiff | Where-Object { $_ -match '^\+[^+]' } | ForEach-Object { $_.Substring(1) }
    if (-not $addedLines) { return @() }

    $secretPatterns = @(
        'AKIA[0-9A-Z]{16}'
        'sk-[a-zA-Z0-9]{20,}'
        'sk-ant-[a-zA-Z0-9\-]{20,}'
        'ghp_[a-zA-Z0-9]{36}'
        'github_pat_[a-zA-Z0-9_]{20,}'
        'AIza[0-9A-Za-z\-_]{35}'
        'xox[baprs]-[0-9a-zA-Z\-]{10,}'
        '-----BEGIN (RSA|EC|OPENSSH|PGP|DSA)? ?PRIVATE KEY-----'
        '(?i)(api[_-]?key|secret|password|token|passwd)\s*[:=]\s*[''"][^''"\s]{8,}[''"]'
    )

    $hits = @()
    foreach ($line in $addedLines) {
        foreach ($pattern in $secretPatterns) {
            if ($line -match $pattern) {
                $snippet = $line.Trim()
                $hits += [PSCustomObject]@{
                    Pattern = $pattern
                    Snippet = $snippet.Substring(0, [Math]::Min(60, $snippet.Length))
                }
                break
            }
        }
    }

    return @($hits)
}

function Get-NovaOptimizerScope {
    param([string[]]$ChangedFiles)

    if ($ChangedFiles | Where-Object { $_ -match '^Views/|MainWindow\.xaml' }) { return "views" }
    if ($ChangedFiles | Where-Object { $_ -match '^Controls/' }) { return "controls" }
    if ($ChangedFiles | Where-Object { $_ -match '^Services/' }) { return "services" }
    if ($ChangedFiles | Where-Object { $_ -match '^Models/' }) { return "models" }
    if ($ChangedFiles | Where-Object { $_ -match '^Native/' }) { return "native" }
    if ($ChangedFiles | Where-Object { $_ -match '^Assets/' }) { return "assets" }
    if ($ChangedFiles | Where-Object { $_ -match 'optimize-services\.ps1|SYSTEM_OPTIMIZATION_LOG\.md' }) { return "optimizer" }
    if ($ChangedFiles | Where-Object { $_ -match 'README\.md|LICENSE' }) { return "docs" }
    if ($ChangedFiles | Where-Object { $_ -match '\.csproj$|app\.manifest|Publish.*\.bat|Run.*\.bat' }) { return "build" }
    if ($ChangedFiles | Where-Object { $_ -match 'sync\.ps1|\.gitignore' }) { return "config" }
    return "general"
}

function Get-AutoCommitMessage {
    $statusLines = git status --porcelain
    if (-not $statusLines) { return $null }

    $modifiedFiles = @()
    $addedFiles = @()
    $deletedFiles = @()
    $allRelativePaths = @()

    foreach ($line in $statusLines) {
        $status = $line.Substring(0, 2).Trim()
        $file = $line.Substring(3).Trim()
        $fileName = Split-Path $file -Leaf
        $allRelativePaths += $file

        if ($status -match 'A|\?\?') { $addedFiles += $fileName }
        elseif ($status -match 'D') { $deletedFiles += $fileName }
        else { $modifiedFiles += $fileName }
    }

    $allChanged = @($addedFiles + $modifiedFiles + $deletedFiles)
    if (@($allChanged).Count -eq 0) { return $null }

    $scope = Get-NovaOptimizerScope -ChangedFiles $allRelativePaths
    $type = "chore"

    if (@($addedFiles).Count -gt 0) {
        $type = "feat"
    }
    elseif ($scope -eq "docs") {
        $type = "docs"
    }
    elseif ($modifiedFiles | Where-Object { $_ -match '\.(cs|xaml)$' }) {
        $type = "refactor"
    }

    $prefix = if ($scope -ne "general") { "${type}(${scope})" } else { "${type}" }

    $summary = ""
    if ($allChanged.Count -le 3) {
        $summary = $allChanged -join ", "
    }
    else {
        $firstTwo = ($allChanged[0..1]) -join ", "
        $extraCount = $allChanged.Count - 2
        $summary = "$firstTwo +$extraCount more"
    }

    $rawDiff = git diff --cached -U0 2>$null
    $diffStat = git diff --cached --shortstat 2>$null
    $churn = ""
    if ($diffStat -match '(\d+) insertion') { $ins = $Matches[1] } else { $ins = 0 }
    if ($diffStat -match '(\d+) deletion') { $del = $Matches[1] } else { $del = 0 }
    if (($ins + 0) -gt 0 -or ($del + 0) -gt 0) { $churn = " (+$ins/-$del)" }

    $hunkContext = $rawDiff |
        Select-String '^@@.*@@\s*(\S.*)$' |
        ForEach-Object { $_.Matches[0].Groups[1].Value } |
        Select-Object -First 1

    if (-not $hunkContext) {
        $addedLine = $rawDiff |
            Select-String '^\+[^+]' |
            ForEach-Object { $_.Line.Substring(1).Trim() } |
            Where-Object { $_.Length -gt 0 } |
            Select-Object -First 1
        if ($addedLine) {
            $snippet = $addedLine
            if ($snippet.Length -gt 45) { $snippet = $snippet.Substring(0, 45) + "..." }
            $hunkContext = $snippet
        }
    }

    if ($hunkContext) {
        return "${prefix}: update ${summary} - ${hunkContext}${churn}"
    }

    return "${prefix}: update ${summary}${churn}"
}

$RepoPath = $PSScriptRoot
if (-not (Test-Path (Join-Path $RepoPath '.git'))) {
    Write-Error "Not a git repository: $RepoPath"
    exit 1
}

Push-Location $RepoPath
try {
    $currentBranch = (git branch --show-current 2>$null)
    if ($currentBranch) { $currentBranch = $currentBranch.Trim() }
    if (-not $currentBranch) { $currentBranch = "main" }

    $hasOrigin = $false
    try {
        git remote get-url origin *> $null
        $hasOrigin = $true
    }
    catch {
        $hasOrigin = $false
    }

    Write-Status -Message "NovaOptimizer Repository: $RepoPath"
    Write-Status -Message "Active Branch: $currentBranch"

    # 1. Pull latest changes if remote origin exists
    if ($hasOrigin) {
        Write-Status -Message "Pulling latest changes from origin/$currentBranch..."
        git pull --rebase --autostash origin $currentBranch
    }
    else {
        Write-Notice -Message "No 'origin' remote configured; skipping pull step."
    }

    if ($PullOnly) {
        Write-Success -Message "Pull complete. No commit was made because -PullOnly was set."
        exit 0
    }

    # 2. Build verification gate
    $csprojFile = Join-Path $RepoPath "NovaOptimizer.csproj"
    if ((-not $SkipBuild) -and (Test-Path $csprojFile) -and (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Status -Message "Running build verification via dotnet build..."
        $buildOutput = & dotnet build $csprojFile --nologo -c Release 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build verification failed. Commit aborted to prevent pushing broken code:`n$buildOutput"
            exit 1
        }
        Write-Success -Message "Build verification passed cleanly."
    }

    # 3. WhatIf preview
    if ($WhatIf) {
        Write-Notice -Message "[WhatIf] Staging status preview:"
        git status -s
        $autoMsg = if ($Message) { $Message } else { Get-AutoCommitMessage }
        Write-Notice -Message "[WhatIf] Commit message would be: '$autoMsg'"
        Write-Success -Message "[WhatIf] Dry-run complete. No changes made."
        exit 0
    }

    # 4. Stage changes
    Write-Status -Message "Staging changes..."
    git add -A

    git diff --cached --quiet 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Success -Message "Working directory clean. Nothing to commit."
        exit 0
    }

    # 5. Secret detection scan
    $secretHits = Find-StagedSecrets
    if (@($secretHits).Count -gt 0) {
        Write-Error "Possible secrets or credentials detected in staged changes! Commit aborted."
        foreach ($hit in @($secretHits)) {
            Write-Host "    Pattern: $($hit.Pattern)" -ForegroundColor Yellow
            Write-Host "    Line   : $($hit.Snippet)..." -ForegroundColor Gray
        }
        Write-Notice -Message "Unstage or remove the secrets before re-running sync."
        git reset
        exit 1
    }

    # 6. Commit message determination
    if (-not $Message) {
        $Message = Get-AutoCommitMessage
        if ($Message) {
            Write-Notice -Message "Auto-generated commit message: '$Message'"
        }
    }

    if ($Message) {
        Write-Status -Message "Committing: '$Message'..."
        git commit -m "$Message"

        # 7. Push to remote
        if ($hasOrigin -and -not $NoPush) {
            Write-Status -Message "Pushing to origin/$currentBranch..."
            git push origin $currentBranch
            if ($LASTEXITCODE -ne 0) {
                Write-Notice -Message "Push rejected. Pulling with rebase and retrying push..."
                git pull --rebase --autostash origin $currentBranch
                git push origin $currentBranch
            }
        }
        elseif ($NoPush) {
            Write-Notice -Message "Commit created locally; push skipped due to -NoPush."
        }
        else {
            Write-Notice -Message "Commit created locally, but no 'origin' remote is configured."
        }

        Write-Success -Message "NovaOptimizer repository synced successfully."
    }
    else {
        Write-Success -Message "No changes to commit."
    }
}
finally {
    Pop-Location
}

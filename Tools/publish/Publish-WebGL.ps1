<#
.SYNOPSIS
  Publishes Builds/WebGL to GitHub Pages via the "gh-pages" branch, classic style: a git worktree checked
  out to an orphan gh-pages branch, mirrored with the build output, committed and pushed. Master history
  stays clean — the build never touches master.

.DESCRIPTION
  One-time setup the USER must do in GitHub's web UI (this script cannot do it):
    Settings > Pages > Build and deployment > Source: "Deploy from a branch"
    Branch: gh-pages, folder: / (root)
  After the first successful run of this script the gh-pages branch will exist and Pages will publish it
  automatically on every push to that branch — no GitHub Actions workflow needed for this approach.

.PARAMETER BuildDir
  Folder produced by VibeGame1.EditorTools.BuildRunner.WebGL() (default: Builds/WebGL at the repo root).

.PARAMETER Branch
  Pages branch to publish to. Default: gh-pages.

.EXAMPLE
  ./Tools/publish/Publish-WebGL.ps1
#>
param(
    [string]$BuildDir = "$PSScriptRoot/../../Builds/WebGL",
    [string]$Branch = "gh-pages"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$worktreeDir = Join-Path $repoRoot ".worktrees/gh-pages"

Push-Location $repoRoot
try {
    $BuildDir = (Resolve-Path $BuildDir -ErrorAction Stop).Path
    if (-not (Test-Path (Join-Path $BuildDir "index.html"))) {
        throw "No index.html in '$BuildDir' — run VibeGame1.EditorTools.BuildRunner.WebGL() in the editor first."
    }

    $sha = (git rev-parse --short HEAD).Trim()
    Write-Host "Publishing $BuildDir to '$Branch' (from master @ $sha)..."

    # Clean any stale worktree from a previous failed run.
    if (Test-Path $worktreeDir) {
        git worktree remove --force $worktreeDir 2>$null
        Remove-Item -Recurse -Force $worktreeDir -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot ".worktrees") | Out-Null

    git fetch origin $Branch 2>$null
    $remoteHasBranch = (git ls-remote --heads origin $Branch) -ne $null -and (git ls-remote --heads origin $Branch) -ne ""

    if ($remoteHasBranch) {
        git worktree add $worktreeDir $Branch
    } else {
        Write-Host "Remote branch '$Branch' does not exist yet — creating it as an orphan."
        git worktree add --detach $worktreeDir
        Push-Location $worktreeDir
        git checkout --orphan $Branch
        git rm -rf --quiet . 2>$null
        Pop-Location
    }

    # Mirror the build into the worktree, keeping its .git metadata.
    Push-Location $worktreeDir
    try {
        Get-ChildItem -Force | Where-Object { $_.Name -ne ".git" } | Remove-Item -Recurse -Force
        Copy-Item -Path (Join-Path $BuildDir "*") -Destination $worktreeDir -Recurse -Force
        # Jekyll would otherwise ignore folders/files starting with an underscore, and Unity's WebGL
        # output can include one; .nojekyll disables that processing entirely.
        New-Item -ItemType File -Force -Path (Join-Path $worktreeDir ".nojekyll") | Out-Null

        git add -A
        $status = git status --porcelain
        if (-not $status) {
            Write-Host "Nothing changed since the last publish — skipping commit."
        } else {
            git commit -m "Playtest WebGL build from master@$sha ($(Get-Date -AsUTC -Format o))"
            git push origin "HEAD:$Branch"
        }
    } finally {
        Pop-Location
    }

    git worktree remove --force $worktreeDir

    $remoteUrl = (git remote get-url origin).Trim()
    Write-Host ""
    Write-Host "Done. If Settings > Pages is set to deploy from '$Branch' / root, the build will be live" -ForegroundColor Green
    Write-Host "at the project's GitHub Pages URL within a minute or two (owner/repo shape, e.g." -ForegroundColor Green
    Write-Host "https://<owner>.github.io/<repo>/ for remote $remoteUrl)." -ForegroundColor Green
}
finally {
    Pop-Location
}

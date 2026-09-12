<#
.SYNOPSIS
  Zips Builds/Windows and attaches it to a tagged GitHub Release. Uses the `gh` CLI if it is on PATH;
  otherwise zips the build and prints the exact manual steps and URL.

.PARAMETER Tag
  Release tag, e.g. "playtest-2026-09-07". Required -- one tag per playtest build keeps bug reports traceable.

.PARAMETER BuildDir
  Folder produced by VibeGame1.EditorTools.BuildRunner.Windows() (default: Builds/Windows at the repo root).

.PARAMETER Title
  Release title. Defaults to the tag.

.EXAMPLE
  ./Tools/publish/Publish-WindowsRelease.ps1 -Tag playtest-2026-09-07
#>
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$BuildDir = "$PSScriptRoot/../../Builds/Windows",
    [string]$Title = $Tag
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path

Push-Location $repoRoot
try {
    $BuildDir = (Resolve-Path $BuildDir -ErrorAction Stop).Path
    $exe = Get-ChildItem -Path $BuildDir -Filter "*.exe" | Select-Object -First 1
    if (-not $exe) {
        throw "No .exe in '$BuildDir' -- run VibeGame1.EditorTools.BuildRunner.Windows() in the editor first."
    }

    $sha = (git rev-parse --short HEAD).Trim()
    $buildInfoPath = Join-Path $BuildDir "build-info.txt"
    if (-not (Test-Path $buildInfoPath)) { throw "No build-info.txt in '$BuildDir' -- refusing an untraceable release." }
    $buildInfo = @{}
    foreach ($line in Get-Content $buildInfoPath) {
        if ($line -match '^([^=]+)=(.*)$') { $buildInfo[$matches[1]] = $matches[2] }
    }
    if ($buildInfo['git_sha'] -ne $sha) {
        throw "Stale Windows build: build-info SHA '$($buildInfo['git_sha'])' does not match HEAD '$sha'. Rebuild first."
    }
    $buildInputsDirty = $buildInfo['build_inputs_dirty']
    if ([string]::IsNullOrWhiteSpace($buildInputsDirty)) { $buildInputsDirty = $buildInfo['git_dirty'] }
    if ($buildInputsDirty -ne 'no') {
        throw "Windows build had dirty or unknown Assets/Packages/ProjectSettings inputs -- commit or remove those changes, rebuild, then release."
    }

    $zipPath = Join-Path (Split-Path $BuildDir -Parent) "vibegame1-windows-$Tag.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Write-Host "Zipping $BuildDir -> $zipPath"
    Compress-Archive -Path (Join-Path $BuildDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

    $remoteUrl = (git remote get-url origin).Trim()
    # git@github.com:Owner/Repo.git -> https://github.com/Owner/Repo
    $httpsUrl = ($remoteUrl -replace '^git@github\.com:', 'https://github.com/') -replace '\.git$', ''

    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        Write-Host "gh CLI found -- creating release '$Tag'."
        git tag $Tag
        git push origin $Tag
        gh release create $Tag $zipPath --title "$Title" --notes "Playtest build. See build-info.txt inside the zip for the exact commit."
        Write-Host "Released: $httpsUrl/releases/tag/$Tag" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "gh CLI not found on PATH. Manual steps:" -ForegroundColor Yellow
        Write-Host "  1. git tag $Tag"
        Write-Host "  2. git push origin $Tag"
        Write-Host "  3. Open $httpsUrl/releases/new?tag=$Tag"
        Write-Host "  4. Title: $Title"
        Write-Host "  5. Drag in the zip: $zipPath"
        Write-Host "  6. Publish release."
    }
}
finally {
    Pop-Location
}

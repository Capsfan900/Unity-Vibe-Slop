param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('status', 'commands', 'state', 'preflight', 'health', 'build-webgl', 'build-windows', 'build-all', 'feature-start', 'feature-poll')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$pathCommand = Get-Command unity -ErrorAction SilentlyContinue
$hubCli = 'C:\Program Files\Unity Hub\resources\cli\unity.exe'
$unityCli = if ($pathCommand) { $pathCommand.Source } elseif (Test-Path $hubCli) { $hubCli } else { $null }

if (-not $unityCli) {
    throw 'Unity CLI was not found on PATH or under the Unity Hub installation.'
}

function Invoke-EditorEval([string]$code, [int]$timeoutSeconds = 30) {
    & $unityCli --format json command --project-path $projectRoot --timeout $timeoutSeconds eval $code
    if ($LASTEXITCODE -ne 0) { throw "Unity CLI eval failed with exit code $LASTEXITCODE." }
}

switch ($Action) {
    'status' {
        & $unityCli --format json pipeline list
    }
    'commands' {
        & $unityCli --format json command --project-path $projectRoot
    }
    'state' {
        Invoke-EditorEval 'return new { playing = UnityEditor.EditorApplication.isPlaying, compiling = UnityEditor.EditorApplication.isCompiling, scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, unity = UnityEngine.Application.unityVersion };'
    }
    'preflight' {
        Invoke-EditorEval 'return VibeGame1.EditorTools.BuildRunner.Preflight();'
    }
    'health' {
        Invoke-EditorEval 'VibeGame1.EditorTools.ProjectHealthCheck.Run(); return "Health Check complete; inspect the structured command result and Editor log.";'
    }
    'build-webgl' {
        Invoke-EditorEval 'return VibeGame1.EditorTools.BuildRunner.WebGL();' 1800
    }
    'build-windows' {
        Invoke-EditorEval 'return VibeGame1.EditorTools.BuildRunner.Windows();' 1800
    }
    'build-all' {
        Invoke-EditorEval 'return VibeGame1.EditorTools.BuildRunner.All();' 3600
    }
    'feature-start' {
        Invoke-EditorEval 'return VibeGame1.EditorTools.FeatureTestRunner.Start();'
    }
    'feature-poll' {
        Invoke-EditorEval 'return VibeGame1.EditorTools.FeatureTestRunner.IsDone() ? VibeGame1.EditorTools.FeatureTestRunner.FullReport() : "RUNNING";'
    }
}

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

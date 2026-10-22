param([string]$Project, [switch]$Preview, [string]$Skill, [string]$CodexHome, [string]$UserSkills)
$ErrorActionPreference = 'Stop'
$env:PYTHONIOENCODING = 'utf-8'
$runtimePython = Join-Path $PSScriptRoot 'runtime\python\python.exe'
try {
    if (-not (Test-Path -LiteralPath $runtimePython)) { throw 'Extract the complete Win64 ZIP first.' }
    & $runtimePython (Join-Path $PSScriptRoot 'tools\verify_package.py') --root $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (-not $Project) {
        Write-Host 'Save and close the target Unity project before installation.'
        $Project = (Read-Host 'Unity project folder (contains Assets and ProjectSettings)').Trim().Trim('"')
    }
    $installArguments = @((Join-Path $PSScriptRoot 'tools\install.py'), '--project', $Project)
    if ($Skill) { $installArguments += @('--skill', $Skill) }
    if ($CodexHome) { $installArguments += @('--codex-home', $CodexHome) }
    if ($UserSkills) { $installArguments += @('--user-skills', $UserSkills) }
    if (-not $Preview) { $installArguments += '--apply' }
    & $runtimePython @installArguments
    exit $LASTEXITCODE
} catch {
    Write-Error $_
    exit 2
}

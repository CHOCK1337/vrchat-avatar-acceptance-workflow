param([string]$Project, [switch]$Preview)
$ErrorActionPreference = 'Stop'
try {
    if (-not $Project) {
        $pointer = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'local\last-install.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $Project = $pointer.project
    }
    $receipt = Get-Content -LiteralPath (Join-Path $Project '.avatar-workbench-codex\install-receipt.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $rollbackArguments = @((Join-Path $PSScriptRoot 'tools\rollback.py'), '--receipt', $receipt.rollback)
    if (-not $Preview) { $rollbackArguments += '--apply' }
    & (Join-Path $PSScriptRoot 'runtime\python\python.exe') @rollbackArguments
    exit $LASTEXITCODE
} catch {
    Write-Error $_
    exit 2
}

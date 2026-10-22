param(
 [string]$Project,
 [string]$UnityMcp,
 [string]$Python,
 [string]$Node,
 [string]$DshHome = $(if($env:DSH_HOME){$env:DSH_HOME}else{Join-Path $env:USERPROFILE '.dsh'}),
 [string]$Profile = 'desktop',
 [string]$Binding,
 [switch]$Apply
)
$ErrorActionPreference='Stop'
if(-not $Project){$Project=(Read-Host 'Unity project folder (contains Assets and ProjectSettings)').Trim('"')}
if(-not $Python){$Python=Join-Path $PSScriptRoot 'runtime\python\python.exe'}
if(-not $Node){$Node=Join-Path $PSScriptRoot 'runtime\node\node.exe'}
if(-not(Test-Path -LiteralPath $Python)){throw 'Extract the COMPLETE Win64 package first. Portable Python is missing.'}
$awDeployArgs=@((Join-Path $PSScriptRoot 'tools\install.py'),'--project',$Project,'--node',$Node,'--dsh-home',$DshHome,'--profile',$Profile)
if($UnityMcp){$awDeployArgs+=@('--unity-mcp',$UnityMcp)}
if($Binding){$awDeployArgs+=@('--binding',$Binding)}
& $Python @awDeployArgs
if($LASTEXITCODE -ne 0){throw 'Preview failed; no update applied.'}
if(-not $Apply){Write-Host 'Preview only. Add -Apply to install this DSH integration.';exit 0}
& $Python @awDeployArgs --apply
if($LASTEXITCODE -ne 0){throw 'Deployment failed.'}
Write-Host 'Open Unity, wait for compilation, run Doctor.cmd, then start a NEW DSH chat.'

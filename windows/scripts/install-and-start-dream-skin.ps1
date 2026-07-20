[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common-windows.ps1')

$codex = Get-DreamSkinCodexInstall
$wasRunning = (Get-DreamSkinCodexProcesses -Codex $codex).Count -gt 0
$closedCodex = $false

try {
  if ($wasRunning) {
    $confirmed = Confirm-DreamSkinRestart -Message 'Codex must close once to install Codex Dream Skin. Unsaved input may be lost. Continue?'
    if (-not $confirmed) {
      Write-Host 'Installation was cancelled; Codex was not changed.'
      exit 0
    }
    Stop-DreamSkinCodex -Codex $codex -AllowForce
    $closedCodex = $true
  }

  & (Join-Path $PSScriptRoot 'install-dream-skin.ps1')
  if ($LASTEXITCODE -ne 0) { throw "install-dream-skin.ps1 failed with exit code $LASTEXITCODE" }

  & (Join-Path $PSScriptRoot 'start-dream-skin.ps1')
  if ($LASTEXITCODE -ne 0) { throw "start-dream-skin.ps1 failed with exit code $LASTEXITCODE" }

  Write-Host 'Codex Dream Skin is installed and active.'
} catch {
  if ($closedCodex -and (Get-DreamSkinCodexProcesses -Codex $codex).Count -eq 0) {
    try { Start-Process -FilePath $codex.Executable | Out-Null } catch {}
  }
  throw
}

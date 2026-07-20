[CmdletBinding()]
param(
  [int]$Port = 9335,
  [switch]$NoShortcuts
)

$ErrorActionPreference = 'Stop'
$PortExplicit = $PSBoundParameters.ContainsKey('Port')
$SkillRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common-windows.ps1')

$operationLock = Enter-DreamSkinOperationLock
try {
  Assert-DreamSkinPort -Port $Port
  $null = Get-DreamSkinNodeRuntime
  $registeredInstalls = @(Get-DreamSkinRegisteredCodexInstalls)
  if ($registeredInstalls.Count -eq 0) {
    throw 'The official OpenAI.Codex Store package is not installed or its identity cannot be validated.'
  }
  foreach ($registeredCodex in $registeredInstalls) {
    if ((Get-DreamSkinCodexProcesses -Codex $registeredCodex).Count -gt 0) {
      throw 'Close Codex before installing Dream Skin so config.toml cannot change during the transaction.'
    }
  }

  $StateRoot = Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
  $StatePath = Join-Path $StateRoot 'state.json'
  $existingState = Read-DreamSkinState -Path $StatePath
  $savedPathCandidate = Get-DreamSkinCodexStatePathCandidate -State $existingState
  $savedCodex = Resolve-DreamSkinCodexInstallFromState -State $existingState -RegisteredInstalls $registeredInstalls
  if ($null -ne $savedPathCandidate -and $null -eq $savedCodex -and
    (Get-DreamSkinCodexProcesses -Codex $savedPathCandidate).Count -gt 0) {
    throw 'The saved Codex path is still running but no longer matches a registered Store package. Close it manually before installing.'
  }
  New-Item -ItemType Directory -Force -Path $StateRoot | Out-Null
  $SkinsRoot = Join-Path $StateRoot 'skins'
  $BuiltInSkinId = 'rose-garden'
  $BundledSkinIds = @($BuiltInSkinId, 'violet-riviera', 'lilac-salon')
  foreach ($skinId in $BundledSkinIds) {
    $sourceCandidates = @(
      (Join-Path $SkillRoot "skins\$skinId"),
      (Join-Path $SkillRoot "bundled-skins\$skinId")
    )
    if ($skinId -eq $BuiltInSkinId) {
      $sourceCandidates += Join-Path $SkillRoot "assets\builtin\$skinId"
    }
    $sourceRoot = $sourceCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
    if (-not $sourceRoot) { throw "Bundled skin source was not found: $skinId" }

    $destination = Join-Path $SkinsRoot $skinId
    $shouldCopy = $skinId -eq $BuiltInSkinId -or -not (Test-Path -LiteralPath $destination -PathType Container)
    if (-not $shouldCopy) { continue }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    foreach ($name in @('skin.json', 'dream-skin.css', 'art.png')) {
      Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination (Join-Path $destination $name) -Force
    }
    $preview = Join-Path $sourceRoot 'preview.png'
    if (-not (Test-Path -LiteralPath $preview -PathType Leaf)) { $preview = Join-Path $sourceRoot 'art.png' }
    Copy-Item -LiteralPath $preview -Destination (Join-Path $destination 'preview.png') -Force
  }
  $ActiveSkinPath = Join-Path $StateRoot 'active-skin.json'
  if (-not (Test-Path -LiteralPath $ActiveSkinPath -PathType Leaf)) {
    Write-DreamSkinUtf8FileAtomically -Path $ActiveSkinPath `
      -Content '{"schemaVersion":1,"skinId":"rose-garden"}'
  }
  $ConfigPath = Join-Path $HOME '.codex\config.toml'
  $BackupPath = Join-Path $StateRoot 'config.before-dream-skin.toml'
  Install-DreamSkinBaseTheme -ConfigPath $ConfigPath -BackupPath $BackupPath

  if (-not $NoShortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    $desktop = [Environment]::GetFolderPath('Desktop')
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $powershell = Get-DreamSkinPowerShellHostPath
    $startScript = Join-Path $PSScriptRoot 'start-dream-skin.ps1'
    $restoreScript = Join-Path $PSScriptRoot 'restore-dream-skin.ps1'
    $portArgument = if ($PortExplicit) { " -Port $Port" } else { '' }

    foreach ($folder in @($desktop, $startMenu)) {
      New-DreamSkinShortcut -Shell $shell -Path (Join-Path $folder 'Codex Dream Skin.lnk') `
        -TargetPath $powershell `
        -Arguments "-NoProfile -ExecutionPolicy Bypass -File `"$startScript`"$portArgument -PromptRestart" `
        -WorkingDirectory $SkillRoot `
        -Description 'Launch the official Codex app with Codex Dream Skin' | Out-Null
    }

    New-DreamSkinShortcut -Shell $shell -Path (Join-Path $desktop 'Codex Dream Skin - Restore.lnk') `
      -TargetPath $powershell `
      -Arguments "-NoProfile -ExecutionPolicy Bypass -File `"$restoreScript`"$portArgument -RestoreBaseTheme -PromptRestart" `
      -WorkingDirectory $SkillRoot `
      -Description 'Restore the official Codex appearance and close the CDP session' | Out-Null

    $managerPath = Join-Path (Split-Path -Parent $SkillRoot) 'Codex皮肤主题管理器.exe'
    if (Test-Path -LiteralPath $managerPath -PathType Leaf) {
      foreach ($folder in @($desktop, $startMenu)) {
        New-DreamSkinShortcut -Shell $shell -Path (Join-Path $folder 'Codex皮肤主题管理器.lnk') `
          -TargetPath $managerPath -Arguments '' -WorkingDirectory (Split-Path -Parent $managerPath) `
          -Description 'Import, preview, and switch Codex Dream Skin themes' | Out-Null
      }
    }
  }

  if ($NoShortcuts) {
    Write-Host 'Codex Dream Skin base theme installed. Run start-dream-skin.ps1 to launch it.'
  } else {
    Write-Host 'Codex Dream Skin installed. The launch shortcut asks before restarting an open Codex window.'
  }
} finally {
  Exit-DreamSkinOperationLock -Mutex $operationLock
}

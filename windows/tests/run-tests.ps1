[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
. (Join-Path $Root 'scripts\common-windows.ps1')

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "codex-dream-skin-tests-$PID-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

try {
  $configPath = Join-Path $temporaryRoot 'config.toml'
  $backupPath = Join-Path $temporaryRoot 'config.before-dream-skin.toml'
  $projectName = -join @([char]0x4EE3, [char]0x7801, [char]0x9879, [char]0x76EE, [char]0x7532)
  $laterValue = -join @([char]0x4FDD, [char]0x7559)
  $sample = "model = `"gpt-5`"`r`n`r`n[other]`r`nappearanceTheme = `"keep-other`"`r`n`r`n[projects.'C:\$projectName']`r`ntrust_level = `"trusted`"`r`n`r`n[desktop]`r`nappearanceTheme = `"system`"`r`nappearanceLightCodeThemeId = `"theme-`$special`"`r`n"
  $utf8NoBom = [System.Text.UTF8Encoding]::new($false, $true)
  [System.IO.File]::WriteAllText($configPath, $sample, $utf8NoBom)
  $originalBytes = [System.IO.File]::ReadAllBytes($configPath)

  Install-DreamSkinBaseTheme -ConfigPath $configPath -BackupPath $backupPath
  $installed = Read-DreamSkinUtf8File -Path $configPath
  if (-not $installed.Contains($projectName) -or $installed -notmatch 'appearanceTheme = "light"') {
    throw 'Install changed a non-ASCII project name or missed the base theme.'
  }
  $backupBytes = [System.IO.File]::ReadAllBytes($backupPath)
  if ([Convert]::ToBase64String($backupBytes) -cne [Convert]::ToBase64String($originalBytes)) {
    throw 'Install did not preserve an exact pre-change config backup.'
  }

  $written = [System.IO.File]::ReadAllBytes($configPath)
  if ($written.Length -ge 3 -and $written[0] -eq 0xEF -and $written[1] -eq 0xBB -and $written[2] -eq 0xBF) {
    throw 'Config writer added an unexpected UTF-8 BOM.'
  }

  $installed += "afterInstall = `"$laterValue`"`r`n"
  Write-DreamSkinUtf8FileAtomically -Path $configPath -Content $installed
  Restore-DreamSkinBaseTheme -ConfigPath $configPath -BackupPath $backupPath
  $restored = Read-DreamSkinUtf8File -Path $configPath
  if (-not $restored.Contains($projectName) -or -not $restored.Contains($laterValue)) {
    throw 'Restore changed a project name or unrelated post-install setting.'
  }
  if ($restored -notmatch 'appearanceTheme = "system"' -or -not $restored.Contains('appearanceLightCodeThemeId = "theme-$special"')) {
    throw 'Restore did not put the original base theme keys back.'
  }
  if ($restored -notmatch '(?ms)^\[other\].*?appearanceTheme = "keep-other"') {
    throw 'Restore changed an appearance key outside the desktop section.'
  }

  $lfConfigPath = Join-Path $temporaryRoot 'config-lf.toml'
  $lfBackupPath = Join-Path $temporaryRoot 'config-lf.before.toml'
  $lfOriginal = "model = `"gpt-5`"`n[projects.'C:\$projectName']`ntrust_level = `"trusted`"`n"
  [System.IO.File]::WriteAllText($lfConfigPath, $lfOriginal, $utf8NoBom)
  Install-DreamSkinBaseTheme -ConfigPath $lfConfigPath -BackupPath $lfBackupPath
  $lfInstalled = Read-DreamSkinUtf8File -Path $lfConfigPath
  if ($lfInstalled.Contains("`r") -or $lfInstalled -notmatch '(?m)^\[desktop\]$') {
    throw 'Install did not preserve LF line endings or create the desktop section.'
  }
  Restore-DreamSkinBaseTheme -ConfigPath $lfConfigPath -BackupPath $lfBackupPath
  $lfRestored = Read-DreamSkinUtf8File -Path $lfConfigPath
  if ($lfRestored.Contains("`r") -or $lfRestored -match '(?m)^\[desktop\]$' -or -not $lfRestored.Contains($projectName)) {
    throw 'Restore did not preserve LF content or remove the generated empty desktop section.'
  }

  $quotedConfigPath = Join-Path $temporaryRoot 'config-quoted.toml'
  $quotedBackupPath = Join-Path $temporaryRoot 'config-quoted.before.toml'
  $quotedOriginal = "[`"desktop`"] # retained comment`r`n`"appearanceTheme`" = `"system`"`r`n'appearanceLightCodeThemeId' = `"theme-`$special`"`r`n"
  [System.IO.File]::WriteAllText($quotedConfigPath, $quotedOriginal, $utf8NoBom)
  Install-DreamSkinBaseTheme -ConfigPath $quotedConfigPath -BackupPath $quotedBackupPath
  $quotedInstalled = Read-DreamSkinUtf8File -Path $quotedConfigPath
  if ([regex]::Matches($quotedInstalled, '(?m)^\s*\[(?:"desktop"|desktop)\]').Count -ne 1) {
    throw 'A commented or quoted desktop table was duplicated during install.'
  }
  Restore-DreamSkinBaseTheme -ConfigPath $quotedConfigPath -BackupPath $quotedBackupPath
  if ((Read-DreamSkinUtf8File -Path $quotedConfigPath) -cne $quotedOriginal) {
    throw 'Quoted desktop keys or a table-header comment were not restored exactly.'
  }

  $singleLineArrayPath = Join-Path $temporaryRoot 'config-single-line-array.toml'
  $singleLineArrayBackup = Join-Path $temporaryRoot 'config-single-line-array.before.toml'
  $singleLineArray = "labels = [`"name[1]`", `"#tag]`"]`r`n"
  [System.IO.File]::WriteAllText($singleLineArrayPath, $singleLineArray, $utf8NoBom)
  Install-DreamSkinBaseTheme -ConfigPath $singleLineArrayPath -BackupPath $singleLineArrayBackup
  if (-not (Read-DreamSkinUtf8File -Path $singleLineArrayPath).Contains($singleLineArray.TrimEnd())) {
    throw 'A safe single-line array containing bracket text was changed or rejected.'
  }

  foreach ($unsupported in @(
    'desktop.appearanceTheme = "system"',
    'desktop = { appearanceTheme = "system" }',
    '[[desktop]]',
    '[desktop.appearanceTheme]',
    '["desktop".layout]',
    '["desk\u0074op".layout]',
    '["desk\u0074op"]',
    "note = `"`"`"fake`r`n[desktop]`r`nappearanceTheme = `"dark`"`r`n`"`"`"",
    "[desktop]`r`nappearanceTheme = [`r`n  `"light`"`r`n]",
    "[desktop]`r`nlayout = [`r`n  [1, 2],`r`n  [3, 4],`r`n]`r`nappearanceTheme = `"dark`"",
    "[desktop]`r`nlayout = [`"]`",`r`n  [`"[`", `"]`"],`r`n]`r`nappearanceTheme = `"dark`""
  )) {
    $unsupportedPath = Join-Path $temporaryRoot ("unsupported-$([guid]::NewGuid().ToString('N')).toml")
    $unsupportedBackup = "$unsupportedPath.before"
    [System.IO.File]::WriteAllText($unsupportedPath, $unsupported, $utf8NoBom)
    $unsupportedRejected = $false
    try { Install-DreamSkinBaseTheme -ConfigPath $unsupportedPath -BackupPath $unsupportedBackup } catch { $unsupportedRejected = $true }
    if (-not $unsupportedRejected -or (Test-Path -LiteralPath $unsupportedBackup)) {
      throw "Unsupported TOML desktop representation was not rejected safely: $unsupported"
    }
  }

  $recoveryPath = Join-Path $temporaryRoot 'config.before-recovery.toml'
  Write-DreamSkinUtf8FileAtomically -Path $configPath -Content 'intentionally changed'
  Restore-DreamSkinConfigBackup -ConfigPath $configPath -BackupPath $backupPath -RecoveryBackupPath $recoveryPath
  $recoveredBytes = [System.IO.File]::ReadAllBytes($configPath)
  if ([Convert]::ToBase64String($recoveredBytes) -cne [Convert]::ToBase64String($originalBytes)) {
    throw 'Exact config recovery did not restore the original bytes.'
  }
  if ((Read-DreamSkinUtf8File -Path $recoveryPath) -cne 'intentionally changed') {
    throw 'Exact config recovery did not preserve the replaced current config.'
  }
  $archivePath = Join-Path $temporaryRoot 'config.restored.toml'
  Archive-DreamSkinConfigBackup -BackupPath $backupPath -ArchivePath $archivePath
  if ((Test-Path -LiteralPath $backupPath) -or -not (Test-Path -LiteralPath $archivePath)) {
    throw 'Completed config backup was not archived for a safe future reinstall.'
  }
  $secondBaseline = "[desktop]`r`nappearanceTheme = `"dark`"`r`n"
  [System.IO.File]::WriteAllText($configPath, $secondBaseline, $utf8NoBom)
  $secondBaselineBytes = [System.IO.File]::ReadAllBytes($configPath)
  Install-DreamSkinBaseTheme -ConfigPath $configPath -BackupPath $backupPath
  if (-not (Test-DreamSkinBytesEqual -Left $secondBaselineBytes -Right ([System.IO.File]::ReadAllBytes($backupPath)))) {
    throw 'Reinstall did not capture a fresh config baseline after completed restore.'
  }
  if (@(Get-ChildItem -LiteralPath $temporaryRoot -Force -Filter '*.replace-backup').Count -ne 0) {
    throw 'Atomic replacement left a temporary replacement backup behind.'
  }

  $invalidPath = Join-Path $temporaryRoot 'invalid.toml'
  $invalidBackupPath = Join-Path $temporaryRoot 'invalid.before.toml'
  [System.IO.File]::WriteAllBytes($invalidPath, [byte[]](0x66, 0x6f, 0x80))
  $rejected = $false
  try { Install-DreamSkinBaseTheme -ConfigPath $invalidPath -BackupPath $invalidBackupPath } catch { $rejected = $true }
  if (-not $rejected -or (Test-Path -LiteralPath $invalidBackupPath)) {
    throw 'Invalid UTF-8 input was not rejected before backup creation.'
  }
  $utf16Path = Join-Path $temporaryRoot 'utf16.toml'
  $utf16BackupPath = Join-Path $temporaryRoot 'utf16.before.toml'
  [System.IO.File]::WriteAllText($utf16Path, 'model = "gpt-5"', [System.Text.Encoding]::Unicode)
  $utf16Rejected = $false
  try { Install-DreamSkinBaseTheme -ConfigPath $utf16Path -BackupPath $utf16BackupPath } catch { $utf16Rejected = $true }
  if (-not $utf16Rejected -or (Test-Path -LiteralPath $utf16BackupPath)) {
    throw 'A UTF-16 config was silently transcoded instead of being rejected.'
  }
  $utf16NoBomPath = Join-Path $temporaryRoot 'utf16-no-bom.toml'
  $utf16NoBomBackupPath = Join-Path $temporaryRoot 'utf16-no-bom.before.toml'
  [System.IO.File]::WriteAllBytes($utf16NoBomPath, [System.Text.Encoding]::Unicode.GetBytes('model = "gpt-5"'))
  $utf16NoBomRejected = $false
  try { Install-DreamSkinBaseTheme -ConfigPath $utf16NoBomPath -BackupPath $utf16NoBomBackupPath } catch { $utf16NoBomRejected = $true }
  if (-not $utf16NoBomRejected -or (Test-Path -LiteralPath $utf16NoBomBackupPath)) {
    throw 'A BOM-less UTF-16 config was silently treated as UTF-8 instead of being rejected.'
  }
  $racePath = Join-Path $temporaryRoot 'race.toml'
  [System.IO.File]::WriteAllText($racePath, 'before', $utf8NoBom)
  $raceExpected = [System.IO.File]::ReadAllBytes($racePath)
  [System.IO.File]::WriteAllText($racePath, 'after', $utf8NoBom)
  $raceRejected = $false
  try { Assert-DreamSkinFileUnchanged -Path $racePath -ExpectedBytes $raceExpected } catch { $raceRejected = $true }
  if (-not $raceRejected) { throw 'Concurrent config modification was not detected.' }
  $conditionalWriteRejected = $false
  try {
    Write-DreamSkinUtf8FileAtomically -Path $racePath -Content 'replacement' -ExpectedBytes $raceExpected
  } catch {
    $conditionalWriteRejected = $true
  }
  if (-not $conditionalWriteRejected -or (Read-DreamSkinUtf8File -Path $racePath) -cne 'after') {
    throw 'Conditional atomic write replaced newer config content.'
  }

  if (-not (Test-DreamSkinWebSocketUrl -Value 'ws://127.0.0.1:9335/devtools/page/test' -Port 9335)) {
    throw 'PowerShell loopback WebSocket validation rejected a safe target.'
  }
  foreach ($unsafe in @(
    'ws://example.com:9335/devtools/page/test',
    'ws://127.0.0.1:9336/devtools/page/test',
    'wss://127.0.0.1:9335/devtools/page/test',
    'ws://user@127.0.0.1:9335/devtools/page/test',
    'ws://127.0.0.1:9335/unexpected/test',
    'ws://127.0.0.1:9335/devtools/page/test?query=1'
  )) {
    if (Test-DreamSkinWebSocketUrl -Value $unsafe -Port 9335) { throw "Accepted unsafe CDP target: $unsafe" }
  }
  $safePageTarget = [pscustomobject]@{
    id = 'page-123'
    type = 'page'
    url = 'app://codex/'
    webSocketDebuggerUrl = 'ws://127.0.0.1:9335/devtools/page/page-123'
  }
  if (-not (Test-DreamSkinCdpPageTarget -Target $safePageTarget -Port 9335)) {
    throw 'A valid same-ID CDP page target was rejected.'
  }
  foreach ($unsafePageTarget in @(
    [pscustomobject]@{ id = 'page-123'; type = 'page'; url = 'app://codex/'; webSocketDebuggerUrl = 'ws://127.0.0.1:9335/devtools/browser/page-123' },
    [pscustomobject]@{ id = 'other-page'; type = 'page'; url = 'app://codex/'; webSocketDebuggerUrl = 'ws://127.0.0.1:9335/devtools/page/page-123' },
    [pscustomobject]@{ id = 123; type = 'page'; url = 'app://codex/'; webSocketDebuggerUrl = 'ws://127.0.0.1:9335/devtools/page/123' },
    [pscustomobject]@{ id = 'page-123'; type = 'other'; url = 'app://codex/'; webSocketDebuggerUrl = 'ws://127.0.0.1:9335/devtools/page/page-123' }
  )) {
    if (Test-DreamSkinCdpPageTarget -Target $unsafePageTarget -Port 9335) {
      throw 'Accepted an inconsistent CDP page target.'
    }
  }
  $watchCommand = '"C:\Program Files\nodejs\node.exe" "C:\Dream Skin\injector.mjs" --watch --port 9335 --browser-id browser-123'
  if (-not (Test-DreamSkinCommandLineToken -CommandLine $watchCommand -Token 'C:\Dream Skin\injector.mjs') -or
    (Test-DreamSkinCommandLineToken -CommandLine $watchCommand -Token 'Dream Skin\injector.mjs')) {
    throw 'Injector command-line token validation is not boundary-safe.'
  }
  if (-not (Test-DreamSkinBrowserId -Value 'browser-123') -or
    (Test-DreamSkinBrowserId -Value 'browser 123')) {
    throw 'CDP browser ID validation is not boundary-safe.'
  }
  $quotedProfile = ConvertTo-DreamSkinProcessArgument -Value '--user-data-dir=C:\Dream Skin\Profile\'
  if ($quotedProfile -cne '"--user-data-dir=C:\Dream Skin\Profile\\"') {
    throw 'Process argument quoting did not protect spaces and a trailing backslash.'
  }

  $statePath = Join-Path $temporaryRoot 'state.json'
  $state = [pscustomobject]@{
    schemaVersion = 3
    platform = 'windows'
    port = 9335
    injectorPid = 1234
    injectorStartedAt = '2026-01-01T00:00:00.0000000Z'
    injectorPath = 'C:\Dream Skin\injector.mjs'
    nodePath = 'C:\Program Files\nodejs\node.exe'
    codexExe = 'C:\Program Files\WindowsApps\OpenAI.Codex\app\ChatGPT.exe'
    codexPackageRoot = 'C:\Program Files\WindowsApps\OpenAI.Codex'
    codexPackageFullName = 'OpenAI.Codex_1.2.3.4_x64__test'
    codexPackageFamilyName = 'OpenAI.Codex_test'
    browserId = 'browser-123'
  }
  Write-DreamSkinState -Path $statePath -State $state
  $loadedState = Read-DreamSkinState -Path $statePath
  if ($loadedState.schemaVersion -ne 3 -or $loadedState.port -ne 9335 -or
    $loadedState.browserId -cne 'browser-123') { throw 'State round-trip failed.' }
  $missingIdentityState = [pscustomobject]@{ schemaVersion = 3; platform = 'windows'; port = 9335 }
  Write-DreamSkinState -Path $statePath -State $missingIdentityState
  $missingIdentityRejected = $false
  try { $null = Read-DreamSkinState -Path $statePath } catch { $missingIdentityRejected = $true }
  if (-not $missingIdentityRejected) { throw 'Schema 3 accepted a state missing process and package identity.' }
  $legacyState = [pscustomobject]@{ schemaVersion = 2; platform = 'windows'; port = 9335; injectorPid = 1234 }
  Write-DreamSkinState -Path $statePath -State $legacyState
  if ((Read-DreamSkinState -Path $statePath).schemaVersion -ne 2) {
    throw 'A supported schema 2 state was rejected.'
  }

  $fakePackageRoot = Join-Path $temporaryRoot 'OpenAI.Codex_1.2.3.4_x64__test'
  $fakeExecutable = Join-Path $fakePackageRoot 'app\ChatGPT.exe'
  New-Item -ItemType Directory -Path (Split-Path -Parent $fakeExecutable) -Force | Out-Null
  [System.IO.File]::WriteAllBytes($fakeExecutable, [byte[]]@())
  $fakePackage = [pscustomobject]@{
    Name = 'OpenAI.Codex'
    InstallLocation = $fakePackageRoot
    PackageFullName = 'OpenAI.Codex_1.2.3.4_x64__test'
    PackageFamilyName = 'OpenAI.Codex_test'
    SignatureKind = 'Store'
    IsDevelopmentMode = $false
    Version = [version]'1.2.3.4'
  }
  $fakeInstall = ConvertTo-DreamSkinCodexInstall -Package $fakePackage
  if ($null -eq $fakeInstall -or $fakeInstall.PackageFullName -cne $fakePackage.PackageFullName -or
    -not (Test-DreamSkinPathEqual -Left $fakeInstall.Executable -Right $fakeExecutable)) {
    throw 'Registered Appx package identity conversion failed.'
  }
  $fakePackage.SignatureKind = 'Developer'
  if ($null -ne (ConvertTo-DreamSkinCodexInstall -Package $fakePackage)) {
    throw 'A non-Store Appx package was accepted as official Codex.'
  }
  $fakePackage.SignatureKind = 'Store'
  $pathOnlyState = [pscustomobject]@{
    codexExe = $fakeExecutable
    codexPackageRoot = $fakePackageRoot
    codexVersion = '1.2.3.4'
  }
  if ($null -eq (Get-DreamSkinCodexStatePathCandidate -State $pathOnlyState)) {
    throw 'A structurally valid legacy Codex path was not recognized for read-only activity checks.'
  }
  if ($null -eq (Resolve-DreamSkinCodexInstallFromState -State $pathOnlyState `
    -RegisteredInstalls @($fakeInstall))) {
    throw 'A legacy state path was not revalidated against a registered Store package.'
  }
  $verifiedPackageState = [pscustomobject]@{
    codexExe = $fakeExecutable
    codexPackageRoot = $fakePackageRoot
    codexVersion = '1.2.3.4'
    codexPackageFullName = $fakePackage.PackageFullName
    codexPackageFamilyName = $fakePackage.PackageFamilyName
  }
  $resolvedInstall = Resolve-DreamSkinCodexInstallFromState -State $verifiedPackageState `
    -RegisteredInstalls @($fakeInstall)
  if ($null -eq $resolvedInstall -or -not $resolvedInstall.RegisteredPackageVerified) {
    throw 'State package identity did not resolve against the registered Appx package.'
  }
  $verifiedPackageState.codexPackageFamilyName = 'OpenAI.Codex_wrong'
  if ($null -ne (Resolve-DreamSkinCodexInstallFromState -State $verifiedPackageState `
    -RegisteredInstalls @($fakeInstall))) {
    throw 'A mismatched Appx package family was accepted from state.'
  }
  Write-DreamSkinUtf8FileAtomically -Path $statePath -Content '[]'
  $badStateRejected = $false
  try { $null = Read-DreamSkinState -Path $statePath } catch { $badStateRejected = $true }
  if (-not $badStateRejected) { throw 'A non-object state file was accepted.' }
  $staleStatePath = Archive-DreamSkinStateFile -Path $statePath
  if ((Test-Path -LiteralPath $statePath) -or -not (Test-Path -LiteralPath $staleStatePath)) {
    throw 'Stale state was not preserved under an archive name.'
  }


  $shortcutDirectory = Join-Path $temporaryRoot 'shortcut-test'
  New-Item -ItemType Directory -Path $shortcutDirectory | Out-Null
  $shortcutPath = Join-Path $shortcutDirectory 'Codex Dream Skin.lnk'
  $shortcutShell = New-Object -ComObject WScript.Shell
  $staleShortcut = $shortcutShell.CreateShortcut($shortcutPath)
  $staleShortcut.TargetPath = $env:ComSpec
  $staleShortcut.WorkingDirectory = $shortcutDirectory
  $staleShortcut.Description = 'stale shortcut'
  $staleShortcut.Save()
  (Get-Item -LiteralPath $shortcutPath).IsReadOnly = $true
  $shortcutArguments = '-NoProfile -ExecutionPolicy Bypass -File "C:\Dream Skin\start.ps1" -PromptRestart'
  New-DreamSkinShortcut -Shell $shortcutShell -Path $shortcutPath -TargetPath $env:ComSpec `
    -Arguments $shortcutArguments -WorkingDirectory $shortcutDirectory -Description 'fresh shortcut' | Out-Null
  $freshShortcut = $shortcutShell.CreateShortcut($shortcutPath)
  if (-not (Test-DreamSkinPathEqual -Left $freshShortcut.TargetPath -Right $env:ComSpec) -or
    $freshShortcut.Arguments -cne $shortcutArguments -or
    -not (Test-DreamSkinPathEqual -Left $freshShortcut.WorkingDirectory -Right $shortcutDirectory) -or
    $freshShortcut.Description -cne 'fresh shortcut' -or
    (Get-Item -LiteralPath $shortcutPath).IsReadOnly) {
    throw 'Shortcut recreation preserved stale Shell Link metadata or wrote incorrect launch properties.'
  }

  $expectedPowerShellHost = (Get-Process -Id $PID -ErrorAction Stop).Path
  if (-not (Test-DreamSkinPathEqual -Left (Get-DreamSkinPowerShellHostPath) -Right $expectedPowerShellHost)) {
    throw 'PowerShell host resolution did not preserve the working installer host.'
  }
  $powerShellLauncher = Join-Path $Root 'scripts\dream-skin-powershell.cmd'
  $launcherOutput = @(& $env:ComSpec /d /c "`"$powerShellLauncher`" -Command `"Write-Output DREAM_SKIN_HOST_OK; exit 19`"" 2>&1)
  $launcherExitCode = $LASTEXITCODE
  if ($launcherExitCode -ne 19 -or ($launcherOutput -join "`n") -notmatch '(?m)^DREAM_SKIN_HOST_OK$') {
    throw "PowerShell launcher did not execute a working host (exit $launcherExitCode): $($launcherOutput -join ' ')"
  }

  $node = Get-DreamSkinNodeRuntime
  $developmentBuiltInRoot = Join-Path $Root 'skins\rose-garden'
  $builtInRoot = if (Test-Path -LiteralPath $developmentBuiltInRoot -PathType Container) {
    $developmentBuiltInRoot
  } else {
    Join-Path $Root 'assets\builtin\rose-garden'
  }
  $rendererPayload = Get-Content -Raw -LiteralPath (Join-Path $Root 'assets\renderer-inject.js')
  $skinCss = Get-Content -Raw -LiteralPath (Join-Path $builtInRoot 'dream-skin.css')
  $roundedShellBlocks = @(
    @{ Name = 'sidebar'; Pattern = '(?s)html\.codex-dream-skin aside\.app-shell-left-panel\s*\{(?<Body>.*?)\}'; Expected = 'border-radius: 20px !important;' },
    @{ Name = 'main surface'; Pattern = '(?s)html\.codex-dream-skin main\.main-surface\s*\{(?<Body>.*?)\}'; Expected = 'border-radius: 20px !important;' },
    @{ Name = 'conversation header'; Pattern = '(?s)html\.codex-dream-skin main\.main-surface > header\.app-header-tint\s*\{(?<Body>.*?)\}'; Expected = 'border-radius: 19px 19px 0 0 !important;' },
    @{ Name = 'decorative chrome'; Pattern = '(?s)#codex-dream-skin-chrome\s*\{(?<Body>.*?)\}'; Expected = 'border-radius: 20px;' }
  )
  foreach ($block in $roundedShellBlocks) {
    $match = [regex]::Match($skinCss, $block.Pattern)
    if (-not $match.Success -or -not $match.Groups['Body'].Value.Contains($block.Expected)) {
      throw "Complete rounded-corner styling is missing for $($block.Name)."
    }
  }
  foreach ($roundedControlMarker in @(
    'html.codex-dream-skin aside.app-shell-left-panel button',
    'html.codex-dream-skin [role="alert"]',
    'html.codex-dream-skin pre',
    'html.codex-dream-skin blockquote'
  )) {
    if (-not $skinCss.Contains($roundedControlMarker)) {
      throw "Rounded control styling is missing marker: $roundedControlMarker"
    }
  }
  foreach ($requiredMarker in @('dream-home-task-mode', 'dream-task-suggestions', 'dream-task-suggestion')) {
    if (-not $rendererPayload.Contains($requiredMarker) -or -not $skinCss.Contains($requiredMarker)) {
      throw "Responsive task-suggestion styling is missing marker: $requiredMarker"
    }
  }
  foreach ($shellMarker in @(
    'dream-windows-menu-bar',
    'data-dream-menu-command-group',
    '--dream-windows-menu-inline-padding',
    '--dream-windows-menu-command-offset',
    'dream-shell-attached-main',
    'dream-shell-attached-sidebar',
    'integrateWindowsShell'
  )) {
    if (-not $rendererPayload.Contains($shellMarker)) {
      throw "Connected Windows shell compatibility is missing marker: $shellMarker"
    }
  }
  foreach ($activityMarker in @('[class~="animate-spin"]:has(> svg[class~="icon-xs"])', 'width: 12px !important;', 'opacity: .16 !important;')) {
    if (-not $rendererPayload.Contains($activityMarker)) {
      throw "Schema v1 sidebar activity styling is missing marker: $activityMarker"
    }
  }
  $injectorSource = Get-Content -Raw -LiteralPath (Join-Path $Root 'scripts\injector.mjs')
  foreach ($shellMarker in @('connectedLeftCorners', 'connectedRightCorners', 'shellSeamPass')) {
    if (-not $injectorSource.Contains($shellMarker)) {
      throw "Connected-shell verification is missing marker: $shellMarker"
    }
  }
  $themeV2Runtime = Get-Content -Raw -LiteralPath (Join-Path $Root 'scripts\theme-v2\runtime\theme-runtime.js')
  foreach ($shellMarker in @(
    'cts-shell-attached-main', 'cts-shell-attached-sidebar', 'integrateShellSeam',
    'data-cts-menu-command-group', '--cts-windows-menu-inline-padding', '--cts-windows-menu-command-offset'
  )) {
    if (-not $themeV2Runtime.Contains($shellMarker)) {
      throw "Theme v2 connected-shell compatibility is missing marker: $shellMarker"
    }
  }
  if (-not $injectorSource.Contains('windowsMenuSeamPass') -or -not $themeV2Runtime.Contains('WINDOWS_MENU_COMMAND_GROUP_ATTR') -or
    -not $themeV2Runtime.Contains('availablePadding') -or -not (Get-Content -Raw -LiteralPath (Join-Path $Root 'scripts\theme-v2\payload.mjs')).Contains('windowsMenuSeamPass')) {
    throw 'Responsive Windows menu seam verification is incomplete.'
  }
  foreach ($activityMarker in @('[class~="animate-spin"]:has(> svg[class~="icon-xs"])', 'width: 12px !important;', 'opacity: .16 !important;')) {
    if (-not $themeV2Runtime.Contains($activityMarker)) {
      throw "Schema v2 sidebar activity styling is missing marker: $activityMarker"
    }
  }
  $violetCssPath = Join-Path $Root 'assets\dream-skin.css'
  if (Test-Path -LiteralPath $violetCssPath -PathType Leaf) {
    $violetCss = Get-Content -Raw -LiteralPath $violetCssPath
    if ($violetCss.Contains('/* Violet Riviera portrait overrides */') -and
      -not $violetCss.Contains('background-position: center, center top !important;')) {
      throw 'Violet Riviera hero does not keep the portrait face inside the wide banner crop.'
    }
  }
  & $node.Path (Join-Path $Root 'scripts\injector.mjs') --self-test *> $null
  if ($LASTEXITCODE -ne 0) { throw 'Injector CDP self-test failed.' }

  $defaultLocalAppData = Join-Path $temporaryRoot 'default-skin-local-app-data'
  New-Item -ItemType Directory -Path $defaultLocalAppData -Force | Out-Null
  $savedLocalAppData = $env:LOCALAPPDATA
  $savedManagedSkinStateRoot = $env:CODEX_DREAM_SKIN_STATE_ROOT
  try {
    $env:LOCALAPPDATA = $defaultLocalAppData
    $env:CODEX_DREAM_SKIN_STATE_ROOT = $null
    $defaultPayloadOutput = @(& $node.Path (Join-Path $Root 'scripts\injector.mjs') --check-payload 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Default skin payload failed: $($defaultPayloadOutput -join ' ')" }
    $defaultPayload = ($defaultPayloadOutput -join "`n") | ConvertFrom-Json
    if (-not $defaultPayload.pass -or $defaultPayload.skinId -cne 'rose-garden') {
      throw 'Injector did not select the isolated built-in skin when no active skin was configured.'
    }
    if ($defaultPayload.starlightEnabled -ne $true) {
      throw 'Injector did not enable starlight effects by default.'
    }
  } finally {
    $env:LOCALAPPDATA = $savedLocalAppData
    $env:CODEX_DREAM_SKIN_STATE_ROOT = $savedManagedSkinStateRoot
  }

  $customLocalAppData = Join-Path $temporaryRoot 'custom-skin-local-app-data'
  $customSkinRoot = Join-Path $customLocalAppData 'CodexDreamSkin\skins\test-skin'
  New-Item -ItemType Directory -Path $customSkinRoot -Force | Out-Null
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $customSkinRoot 'skin.json') -Content `
    '{"schemaVersion":1,"id":"test-skin","name":"Test Skin","version":"1.0.0","author":"Tests","description":"Fixture","brandName":"Test Skin","brandSubtitle":"Fixture","signature":"Test"}'
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $customSkinRoot 'dream-skin.css') -Content `
    'html.codex-dream-skin { --dream-test-skin: 1; }'
  Copy-Item -LiteralPath (Join-Path $builtInRoot 'art.png') -Destination (Join-Path $customSkinRoot 'art.png')
  $customStateRoot = Join-Path $customLocalAppData 'CodexDreamSkin'
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $customStateRoot 'active-skin.json') -Content `
    '{"schemaVersion":1,"skinId":"test-skin","starlightEnabled":false}'
  $savedLocalAppData = $env:LOCALAPPDATA
  $savedManagedSkinStateRoot = $env:CODEX_DREAM_SKIN_STATE_ROOT
  try {
    $env:LOCALAPPDATA = $customLocalAppData
    $env:CODEX_DREAM_SKIN_STATE_ROOT = $null
    $customPayloadOutput = @(& $node.Path (Join-Path $Root 'scripts\injector.mjs') --check-payload 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Custom skin payload failed: $($customPayloadOutput -join ' ')" }
    $customPayload = ($customPayloadOutput -join "`n") | ConvertFrom-Json
    if (-not $customPayload.pass -or $customPayload.skinId -cne 'test-skin') {
      throw 'Injector did not select the managed active skin.'
    }
    if ($customPayload.starlightEnabled -ne $false) {
      throw 'Injector did not pass the managed starlight preference into the payload.'
    }
    Write-DreamSkinUtf8FileAtomically -Path (Join-Path $customSkinRoot 'dream-skin.css') -Content `
      '@import url("https://example.com/unsafe.css");'
    & $node.Path (Join-Path $Root 'scripts\injector.mjs') --check-payload *> $null
    if ($LASTEXITCODE -eq 0) { throw 'Injector accepted skin CSS containing a remote import.' }
  } finally {
    $env:LOCALAPPDATA = $savedLocalAppData
    $env:CODEX_DREAM_SKIN_STATE_ROOT = $savedManagedSkinStateRoot
  }

  $portableStateRoot = Join-Path $temporaryRoot 'codex-skin-manager'
  $portableSkinRoot = Join-Path $portableStateRoot 'skin\portable-test'
  New-Item -ItemType Directory -Path $portableSkinRoot -Force | Out-Null
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $portableSkinRoot 'skin.json') -Content `
    '{"schemaVersion":1,"id":"portable-test","name":"Portable Test","version":"1.0.0","author":"Tests","description":"Fixture","brandName":"Portable Test","brandSubtitle":"Fixture","signature":"Test"}'
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $portableSkinRoot 'dream-skin.css') -Content `
    'html.codex-dream-skin { --dream-portable-skin: 1; }'
  Copy-Item -LiteralPath (Join-Path $builtInRoot 'art.png') -Destination (Join-Path $portableSkinRoot 'art.png')
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $portableStateRoot 'active-skin.json') -Content `
    '{"schemaVersion":1,"skinId":"portable-test","starlightEnabled":false}'
  $savedPortableStateRoot = $env:CODEX_DREAM_SKIN_STATE_ROOT
  try {
    $env:CODEX_DREAM_SKIN_STATE_ROOT = $portableStateRoot
    $portablePayloadOutput = @(& $node.Path (Join-Path $Root 'scripts\injector.mjs') --check-payload 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Portable skin payload failed: $($portablePayloadOutput -join ' ')" }
    $portablePayload = ($portablePayloadOutput -join "`n") | ConvertFrom-Json
    if (-not $portablePayload.pass -or $portablePayload.skinId -cne 'portable-test') {
      throw 'Injector did not select the skin from codex-skin-manager\skin.'
    }
    if ($portablePayload.starlightEnabled -ne $false) {
      throw 'Injector did not pass the portable starlight preference into the payload.'
    }
  } finally {
    $env:CODEX_DREAM_SKIN_STATE_ROOT = $savedPortableStateRoot
  }

  $awesomeStateRoot = Join-Path $temporaryRoot 'awesome-v2-state'
  $awesomeThemeRoot = Join-Path $awesomeStateRoot 'skin\awesome-v2-test'
  New-Item -ItemType Directory -Path $awesomeThemeRoot -Force | Out-Null
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $awesomeThemeRoot 'theme.json') -Content `
    '{"schemaVersion":2,"id":"awesome-v2-test","name":"Awesome V2 Test","version":"1.0.0","author":"Tests","description":"Fixture","appearance":"dual","css":"theme.css","chrome":"chrome.html","assets":{}}'
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $awesomeThemeRoot 'theme.css') -Content `
    'html.codex-theme-studio { --awesome-v2-test: 1; }'
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $awesomeThemeRoot 'chrome.html') -Content `
    '<div data-cts-layer="stage"><i data-cts-text="hero-title"></i></div>'
  Write-DreamSkinUtf8FileAtomically -Path (Join-Path $awesomeStateRoot 'active-skin.json') -Content `
    '{"schemaVersion":1,"skinId":"awesome-v2-test","starlightEnabled":false}'
  $savedAwesomeStateRoot = $env:CODEX_DREAM_SKIN_STATE_ROOT
  try {
    $env:CODEX_DREAM_SKIN_STATE_ROOT = $awesomeStateRoot
    $awesomePayloadOutput = @(& $node.Path (Join-Path $Root 'scripts\injector.mjs') --check-payload 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Awesome v2 payload failed: $($awesomePayloadOutput -join ' ')" }
    $awesomePayload = ($awesomePayloadOutput -join "`n") | ConvertFrom-Json
    if (-not $awesomePayload.pass -or $awesomePayload.skinId -cne 'awesome-v2-test' -or
      $awesomePayload.format -cne 'awesome-v2') {
      throw 'Injector did not select the awesome schema v2 runtime.'
    }
    if ($awesomePayload.starlightEnabled -ne $false) {
      throw 'Awesome schema v2 payload did not preserve the disabled starlight preference.'
    }
    Write-DreamSkinUtf8FileAtomically -Path (Join-Path $awesomeThemeRoot 'theme.css') -Content `
      '@import url("https://example.com/unsafe-v2.css");'
    & $node.Path (Join-Path $Root 'scripts\injector.mjs') --check-payload *> $null
    if ($LASTEXITCODE -eq 0) { throw 'Injector accepted unsafe awesome schema v2 CSS.' }
  } finally {
    $env:CODEX_DREAM_SKIN_STATE_ROOT = $savedAwesomeStateRoot
  }

  Write-Host 'PASS: config transactions, restore scoping, state safety, argument quoting, and loopback CDP validation.'
} finally {
  Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}

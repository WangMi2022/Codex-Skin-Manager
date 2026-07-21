[CmdletBinding()]
param(
  [string]$OutputPath,
  [string]$Version = '2.5.4',
  [string]$NodeVersion
)

$ErrorActionPreference = 'Stop'
$windowsRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $windowsRoot
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot "release\Codex-Dream-Skin-Setup-Windows-x64-v$Version.exe" }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "codex-dream-skin-installer-$PID-$([guid]::NewGuid().ToString('N'))"
$testParent = Join-Path ([System.IO.Path]::GetTempPath()) "csm-test-$PID-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$payloadRoot = Join-Path $temporaryRoot 'payload'
$payloadZip = Join-Path $temporaryRoot 'payload.zip'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Copy-ReleaseFiles {
  param([string]$SourceRoot, [string]$DestinationRoot, [string[]]$Names)
  [System.IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null
  foreach ($name in $Names) {
    $source = Join-Path $SourceRoot $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Release file was not found: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $DestinationRoot $name) -Force
  }
}

function Copy-ReleaseDirectory {
  param([string]$SourceRoot, [string]$DestinationRoot)
  if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) { throw "Release directory was not found: $SourceRoot" }
  $sourceFull = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
  [System.IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null
  foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -File -Recurse) {
    $relative = $file.FullName.Substring($sourceFull.Length)
    $destination = Join-Path $DestinationRoot $relative
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
  }
}

function Find-CSharpCompiler {
  @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
  ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

function Compile-WinExe {
  param([string]$Compiler, [string]$Source, [string]$Output, [string]$Icon, [object[]]$Resources = @(), [string[]]$References = @())
  $arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/debug-', '/langversion:5',
    "/out:$Output", "/win32icon:$Icon", "/win32manifest:$(Join-Path $windowsRoot 'manager\app.manifest')")
  $arguments += $References | ForEach-Object { "/reference:$_" }
  $arguments += $Resources | ForEach-Object { "/resource:$($_.Path),$($_.Name)" }
  $arguments += $Source
  & $Compiler @arguments
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Output -PathType Leaf)) { throw "C# compilation failed for $Output (exit $LASTEXITCODE)." }
}

$bundledCatalogPath = Join-Path $windowsRoot 'skins\bundled-skins.json'
if (-not (Test-Path -LiteralPath $bundledCatalogPath -PathType Leaf)) { throw "Bundled skin catalog was not found: $bundledCatalogPath" }
$bundledCatalog = Get-Content -Raw -LiteralPath $bundledCatalogPath | ConvertFrom-Json
$bundledSkinIds = @($bundledCatalog.skins | ForEach-Object { [string]$_.id })
if ($bundledCatalog.schemaVersion -ne 1 -or $bundledSkinIds.Count -ne 29 -or
  @($bundledSkinIds | Sort-Object -Unique).Count -ne $bundledSkinIds.Count -or $bundledSkinIds -notcontains 'rose-garden') {
  throw 'Bundled skin catalog must contain the 29 unique offline skins including rose-garden.'
}

try {
  [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
  $hostExecutable = (Get-Process -Id $PID -ErrorAction Stop).Path
  & $hostExecutable -NoProfile -File (Join-Path $windowsRoot 'tests\run-tests.ps1')
  if ($LASTEXITCODE -ne 0) { throw "Windows regression tests failed with exit code $LASTEXITCODE" }

  $node = Get-Command node.exe -ErrorAction SilentlyContinue
  if (-not $node) { $node = Get-Command node -ErrorAction Stop }
  if (-not $NodeVersion) { $NodeVersion = "$(& $node.Source -p 'process.versions.node')".Trim() }
  if ($NodeVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid Node.js version: $NodeVersion" }

  $nodeArchiveName = "node-v$NodeVersion-win-x64.zip"
  $nodeArchivePath = Join-Path $temporaryRoot $nodeArchiveName
  $checksumsPath = Join-Path $temporaryRoot 'SHASUMS256.txt'
  Invoke-WebRequest -Uri "https://nodejs.org/dist/v$NodeVersion/$nodeArchiveName" -OutFile $nodeArchivePath
  Invoke-WebRequest -Uri "https://nodejs.org/dist/v$NodeVersion/SHASUMS256.txt" -OutFile $checksumsPath
  $checksumLine = Get-Content -LiteralPath $checksumsPath | Where-Object { $_ -match "\s$([regex]::Escape($nodeArchiveName))$" } | Select-Object -First 1
  if (-not $checksumLine -or $checksumLine -notmatch '^([0-9a-fA-F]{64})\s+') { throw "Official checksum was not found for $nodeArchiveName" }
  $expectedNodeHash = $Matches[1].ToLowerInvariant()
  $actualNodeHash = (Get-FileHash -LiteralPath $nodeArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualNodeHash -cne $expectedNodeHash) { throw 'Downloaded Node.js archive failed SHA-256 validation.' }
  $nodeExtractRoot = Join-Path $temporaryRoot 'node-extracted'
  Expand-Archive -LiteralPath $nodeArchivePath -DestinationPath $nodeExtractRoot
  $nodeSourceRoot = Join-Path $nodeExtractRoot "node-v$NodeVersion-win-x64"

  [System.IO.Directory]::CreateDirectory($payloadRoot) | Out-Null
  $managerOutput = Join-Path $payloadRoot 'Codex皮肤主题管理器.exe'
  & $hostExecutable -NoProfile -File (Join-Path $windowsRoot 'manager\build-manager.ps1') -OutputPath $managerOutput
  if ($LASTEXITCODE -ne 0) { throw "Skin Manager build failed with exit code $LASTEXITCODE" }

  $compiler = Find-CSharpCompiler
  if (-not $compiler) { throw 'Windows .NET Framework C# compiler was not found.' }
  $icon = Join-Path $windowsRoot 'manager\dream-skin-manager.ico'
  $manifest = Join-Path $windowsRoot 'manager\app.manifest'
  $uninstallerOutput = Join-Path $payloadRoot 'Codex皮肤主题管理器卸载程序.exe'
  Compile-WinExe -Compiler $compiler -Source (Join-Path $PSScriptRoot '..\installer\Uninstaller.cs') -Output $uninstallerOutput -Icon $icon `
    -References @('System.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')

  $engine = Join-Path $payloadRoot '.codex-dream-skin'
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'assets') -DestinationRoot (Join-Path $engine 'assets') -Names @('renderer-inject.js')
  foreach ($skinId in $bundledSkinIds) {
    $skinDestination = if ($skinId -eq 'rose-garden') {
      Join-Path $engine "assets\builtin\$skinId"
    } else {
      Join-Path $engine "bundled-skins\$skinId"
    }
    Copy-ReleaseDirectory -SourceRoot (Join-Path $windowsRoot "skins\$skinId") -DestinationRoot $skinDestination
  }
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'skins') -DestinationRoot (Join-Path $engine 'bundled-skins') -Names @('bundled-skins.json')
  Move-Item -LiteralPath (Join-Path $engine 'bundled-skins\bundled-skins.json') -Destination (Join-Path $engine 'bundled-skins\catalog.json') -Force
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'scripts') -DestinationRoot (Join-Path $engine 'scripts') -Names @(
    'common-windows.ps1', 'config-utf8.ps1', 'injector.mjs', 'install-and-start-dream-skin.ps1',
    'install-dream-skin.ps1', 'restore-dream-skin.ps1', 'start-dream-skin.ps1', 'verify-dream-skin.ps1',
    'dream-skin-powershell.cmd'
  )
  Copy-Item -LiteralPath (Join-Path $windowsRoot 'scripts\theme-v2') -Destination (Join-Path $engine 'scripts\theme-v2') -Recurse -Force
  Copy-ReleaseFiles -SourceRoot $nodeSourceRoot -DestinationRoot (Join-Path $engine 'runtime\node') -Names @('node.exe', 'LICENSE', 'README.md')
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'runtime\webp') -DestinationRoot (Join-Path $engine 'runtime\webp') -Names @('dwebp.exe', 'LICENSE.libwebp.txt', 'README.txt')
  Copy-ReleaseFiles -SourceRoot $repoRoot -DestinationRoot $payloadRoot -Names @('THIRD_PARTY_NOTICES.md')
  Copy-ReleaseDirectory -SourceRoot (Join-Path $windowsRoot 'third-party\awesome-codex-skins') `
    -DestinationRoot (Join-Path $payloadRoot 'THIRD_PARTY\awesome-codex-skins')
  $dwebpHash = (Get-FileHash -LiteralPath (Join-Path $engine 'runtime\webp\dwebp.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($dwebpHash -cne 'ee66951df0f868f0c41f49fcc2d0fc53072912b7357836317ca177cbae5eb343') { throw 'Bundled dwebp.exe failed SHA-256 validation.' }
  [System.IO.File]::WriteAllText((Join-Path $payloadRoot 'VERSION'), "$Version`r`n", $utf8NoBom)
  [System.IO.File]::WriteAllText((Join-Path $payloadRoot 'README.txt'), "Codex皮肤主题管理器 $Version`r`n`r`n离线内置 29 套皮肤（含 awesome-codex-skins skins-v1.1.0 的全部 26 套认证皮肤），支持 schema v1 与 schema v2。打开 Codex皮肤主题管理器.exe 即可预览、切换、导入、重命名和导出皮肤。引用现有 IP 的上游同人皮肤仅供个人非商业使用，详见 THIRD_PARTY\awesome-codex-skins\DISCLAIMER.md。`r`n", $utf8NoBom)

  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [System.IO.Compression.ZipFile]::CreateFromDirectory($payloadRoot, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
  [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
  $installerSource = Join-Path $PSScriptRoot '..\installer\Installer.cs'
  $installerOutput = $OutputPath
  $resourcePayload = [pscustomobject]@{ Path = $payloadZip; Name = 'CodexDreamSkin.Payload.zip' }
  $resourceLogo = [pscustomobject]@{ Path = (Join-Path $windowsRoot 'manager\dream-skin-manager.png'); Name = 'CodexDreamSkin.Logo.png' }
  Compile-WinExe -Compiler $compiler -Source $installerSource -Output $installerOutput -Icon $icon `
    -Resources @($resourcePayload, $resourceLogo) -References @(
      'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
      'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Web.Extensions.dll', 'Microsoft.CSharp.dll'
    )

  $buttonRenderShot = Join-Path $temporaryRoot 'installer-button-render-125.png'
  $buttonRenderTest = Start-Process -FilePath $installerOutput `
    -ArgumentList @('--test-button-render', $buttonRenderShot, '--scale', '1.25') -PassThru -Wait
  if ($buttonRenderTest.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $buttonRenderShot -PathType Leaf)) {
    throw 'Installer high-DPI button rendering regression test failed.'
  }

  $moveRetryRoot = Join-Path $temporaryRoot 'directory-move-retry-test'
  $moveRetryErrorLog = Join-Path $temporaryRoot 'directory-move-retry-error.txt'
  $moveRetryTest = Start-Process -FilePath $installerOutput `
    -ArgumentList @('--test-directory-move-retry', $moveRetryRoot, '--test-error-log', $moveRetryErrorLog) -PassThru -Wait
  if ($moveRetryTest.ExitCode -ne 0) {
    $moveRetryError = if (Test-Path -LiteralPath $moveRetryErrorLog -PathType Leaf) { Get-Content -Raw -LiteralPath $moveRetryErrorLog } else { '' }
    throw "Installer transient directory-lock regression test failed: $moveRetryError"
  }

  $longTestParent = Join-Path $testParent ("deep-" + ('x' * 80))
  $longTestErrorLog = Join-Path $testParent 'long-path-error.txt'
  $longPathTest = Start-Process -FilePath $installerOutput `
    -ArgumentList @('--test-install-parent', $longTestParent, '--test-error-log', $longTestErrorLog) -PassThru -Wait
  $longPathError = if (Test-Path -LiteralPath $longTestErrorLog -PathType Leaf) { Get-Content -Raw -LiteralPath $longTestErrorLog } else { '' }
  if ($longPathTest.ExitCode -eq 0 -or $longPathError -notmatch '安装路径过深') {
    throw "Installer did not reject a path that exceeds the extraction budget: $longPathError"
  }

  $testRoot = Join-Path $testParent 'codex-skin-manager'
  $testErrorLog = Join-Path $temporaryRoot 'installer-smoke-error.txt'
  $test = Start-Process -FilePath $installerOutput -ArgumentList @('--test-install-parent', $testParent, '--test-error-log', $testErrorLog) -PassThru -Wait
  if ($test.ExitCode -ne 0) {
    $testError = if (Test-Path -LiteralPath $testErrorLog -PathType Leaf) { Get-Content -Raw -LiteralPath $testErrorLog } else { 'No error log was written.' }
    throw "Installer smoke test failed with exit code $($test.ExitCode): $testError"
  }
  foreach ($required in @(
    (Join-Path $testRoot 'Codex皮肤主题管理器.exe'),
    (Join-Path $testRoot 'Codex皮肤主题管理器卸载程序.exe'),
    (Join-Path $testRoot '.codex-dream-skin\assets\renderer-inject.js'),
    (Join-Path $testRoot '.codex-dream-skin\scripts\theme-v2\payload.mjs'),
    (Join-Path $testRoot '.codex-dream-skin\runtime\webp\dwebp.exe'),
    (Join-Path $testRoot 'active-skin.json')
  )) { if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Installer smoke test missing: $required" } }
  foreach ($obsolete in @('Codex Dream Skin Manager.exe', 'Codex Dream Skin Uninstaller.exe')) {
    if (Test-Path -LiteralPath (Join-Path $testRoot $obsolete)) { throw "Installer smoke test found obsolete file: $obsolete" }
  }
  $installedManagerTest = Start-Process -FilePath (Join-Path $testRoot 'Codex皮肤主题管理器.exe') `
    -ArgumentList @('--self-test', (Join-Path $testRoot '.codex-dream-skin')) -PassThru -Wait
  if ($installedManagerTest.ExitCode -ne 0) { throw "Installed manager v1/v2 self-test failed with exit code $($installedManagerTest.ExitCode)" }
  $active = Get-Content -Raw -LiteralPath (Join-Path $testRoot 'active-skin.json') | ConvertFrom-Json
  if ($active.skinId -cne 'rose-garden' -or $active.starlightEnabled -ne $true) {
    throw 'Installer smoke test selected the wrong default skin or disabled dynamic effects.'
  }
  foreach ($skinId in $bundledSkinIds) {
    $skinRoot = Join-Path $testRoot "skin\$skinId"
    $hasV1 = Test-Path -LiteralPath (Join-Path $skinRoot 'skin.json') -PathType Leaf
    $hasV2 = Test-Path -LiteralPath (Join-Path $skinRoot 'theme.json') -PathType Leaf
    if ($hasV1 -eq $hasV2) { throw "Installer smoke test did not seed a valid $skinId" }
    if ($hasV2 -and -not (Test-Path -LiteralPath (Join-Path $skinRoot '.manager-preview.png') -PathType Leaf)) {
      throw "Installer smoke test did not generate a WinForms-compatible preview for $skinId"
    }
  }
  if (@(Get-ChildItem -LiteralPath (Join-Path $testRoot 'skin') -Directory).Count -ne $bundledSkinIds.Count) {
    throw 'Installer smoke test seeded the wrong number of bundled skins.'
  }
  $holderScript = Join-Path $temporaryRoot 'hold-process.mjs'
  [System.IO.File]::WriteAllText($holderScript, 'setInterval(() => {}, 1000);', $utf8NoBom)
  $runtimeNode = Join-Path $testRoot '.codex-dream-skin\runtime\node\node.exe'

  $fakeCodexExe = Join-Path $temporaryRoot 'OpenAI.Codex_test\app\ChatGPT.exe'
  [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fakeCodexExe)) | Out-Null
  Copy-Item -LiteralPath $runtimeNode -Destination $fakeCodexExe -Force
  $fakeCodex = $null
  try {
    $fakeCodex = Start-Process -FilePath $fakeCodexExe -ArgumentList @("`"$holderScript`"") -PassThru
    Start-Sleep -Milliseconds 400
    if ($fakeCodex.HasExited) { throw 'Synthetic Codex process exited before the installer activity check.' }
    Remove-Item -LiteralPath $testErrorLog -Force -ErrorAction SilentlyContinue
    $codexGuard = Start-Process -FilePath $installerOutput `
      -ArgumentList @('--test-check-codex-path', $fakeCodexExe, '--test-error-log', $testErrorLog) -PassThru -Wait
    $codexGuardError = if (Test-Path -LiteralPath $testErrorLog -PathType Leaf) { Get-Content -Raw -LiteralPath $testErrorLog } else { '' }
    if ($codexGuard.ExitCode -eq 0 -or $codexGuardError -notmatch 'Codex 客户端仍在运行') {
      throw "Installer did not block a running Codex client: $codexGuardError"
    }
  } finally {
    if ($null -ne $fakeCodex -and -not $fakeCodex.HasExited) {
      Stop-Process -Id $fakeCodex.Id -Force -ErrorAction SilentlyContinue
      Wait-Process -Id $fakeCodex.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
  }

  $preservedMarker = Join-Path $testRoot 'skin\imported-test\preserved.txt'
  [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($preservedMarker)) | Out-Null
  [System.IO.File]::WriteAllText($preservedMarker, 'preserve imported skin', $utf8NoBom)
  $removedCoralMarker = Join-Path $testRoot 'skin\coral-haze\skin.json'
  [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($removedCoralMarker)) | Out-Null
  [System.IO.File]::WriteAllText($removedCoralMarker, '{"id":"coral-haze"}', $utf8NoBom)
  $runtimeHolder = $null
  try {
    $runtimeHolder = Start-Process -FilePath $runtimeNode -ArgumentList @("`"$holderScript`"") -PassThru
    Start-Sleep -Milliseconds 400
    if ($runtimeHolder.HasExited) { throw 'Synthetic skin injector exited before the installer activity check.' }

    Remove-Item -LiteralPath $testErrorLog -Force -ErrorAction SilentlyContinue
    $runtimeGuard = Start-Process -FilePath $installerOutput `
      -ArgumentList @('--test-check-runtime-root', $testRoot, '--test-error-log', $testErrorLog) -PassThru -Wait
    $runtimeGuardError = if (Test-Path -LiteralPath $testErrorLog -PathType Leaf) { Get-Content -Raw -LiteralPath $testErrorLog } else { '' }
    if ($runtimeGuard.ExitCode -eq 0 -or $runtimeGuardError -notmatch '皮肤注入引擎仍在运行' -or
      -not (Test-Path -LiteralPath $preservedMarker -PathType Leaf)) {
      throw "Installer did not block the running skin injector before modifying storage: $runtimeGuardError"
    }
  } finally {
    if ($null -ne $runtimeHolder -and -not $runtimeHolder.HasExited) {
      Stop-Process -Id $runtimeHolder.Id -Force -ErrorAction SilentlyContinue
      Wait-Process -Id $runtimeHolder.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
  }

  $cleanupBlocker = Join-Path $testRoot 'readonly-cleanup-blocker.txt'
  [System.IO.File]::WriteAllText($cleanupBlocker, 'retain old backup when cleanup is temporarily blocked', $utf8NoBom)
  (Get-Item -LiteralPath $cleanupBlocker).IsReadOnly = $true
  Remove-Item -LiteralPath $testErrorLog -Force -ErrorAction SilentlyContinue
  $upgradeTest = Start-Process -FilePath $installerOutput `
    -ArgumentList @('--test-install-parent', $testParent, '--test-error-log', $testErrorLog) -PassThru -Wait
  if ($upgradeTest.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $preservedMarker -PathType Leaf) -or
    (Test-Path -LiteralPath $removedCoralMarker)) {
    $upgradeError = if (Test-Path -LiteralPath $testErrorLog -PathType Leaf) { Get-Content -Raw -LiteralPath $testErrorLog } else { '' }
    throw "Installer upgrade storage test failed with exit code $($upgradeTest.ExitCode): $upgradeError"
  }
  $retainedBackups = @(Get-ChildItem -LiteralPath $testParent -Directory -Filter 'codex-skin-manager.backup-*')
  if ($retainedBackups.Count -eq 0) {
    throw 'Installer cleanup test did not retain the locked old-version backup for later cleanup.'
  }
  foreach ($retainedBackup in $retainedBackups) {
    if (-not $retainedBackup.FullName.StartsWith($testParent, [System.StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing to clean an installer test backup outside the test root: $($retainedBackup.FullName)"
    }
    $retainedBlocker = Join-Path $retainedBackup.FullName 'readonly-cleanup-blocker.txt'
    if (Test-Path -LiteralPath $retainedBlocker -PathType Leaf) { (Get-Item -LiteralPath $retainedBlocker).IsReadOnly = $false }
    Remove-Item -LiteralPath $retainedBackup.FullName -Recurse -Force
  }
  $uninstallerSmoke = Join-Path $temporaryRoot 'uninstaller-smoke.exe'
  Copy-Item -LiteralPath (Join-Path $testRoot 'Codex皮肤主题管理器卸载程序.exe') -Destination $uninstallerSmoke -Force
  $uninstallTest = Start-Process -FilePath $uninstallerSmoke -ArgumentList @('--test-root', $testRoot) -PassThru -Wait
  $managerRemains = Test-Path -LiteralPath (Join-Path $testRoot 'Codex皮肤主题管理器.exe')
  $engineRemains = Test-Path -LiteralPath (Join-Path $testRoot '.codex-dream-skin')
  if ($uninstallTest.ExitCode -ne 0 -or $managerRemains -or $engineRemains) {
    throw "Uninstaller smoke test failed with exit code $($uninstallTest.ExitCode); manager remains: $managerRemains; engine remains: $engineRemains"
  }
  if (-not (Test-Path -LiteralPath $preservedMarker -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $testRoot 'active-skin.json') -PathType Leaf)) {
    throw 'Uninstaller smoke test removed the user skin state.'
  }
  if (Test-Path -LiteralPath $testParent) { [System.IO.Directory]::Delete($testParent, $true) }

  $hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
  [System.IO.File]::WriteAllText("$OutputPath.sha256", "$hash  $([System.IO.Path]::GetFileName($OutputPath))`r`n", $utf8NoBom)
  Write-Host "Created: $OutputPath"
  Write-Host "SHA-256: $hash"
  Write-Host "Node.js: $NodeVersion ($expectedNodeHash)"
} finally {
  if (Test-Path -LiteralPath $testParent) {
    $resolvedTestParent = [System.IO.Path]::GetFullPath($testParent)
    $tempPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTestParent.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing to clean installer test data outside the temporary directory: $resolvedTestParent"
    }
    Get-ChildItem -LiteralPath $resolvedTestParent -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object {
      if ($_.IsReadOnly) { $_.IsReadOnly = $false }
    }
    [System.IO.Directory]::Delete($resolvedTestParent, $true)
  }
  if (Test-Path -LiteralPath $temporaryRoot) { [System.IO.Directory]::Delete($temporaryRoot, $true) }
}

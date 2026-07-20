[CmdletBinding()]
param(
  [string]$OutputPath,
  [string]$Version = '2.3.3',
  [string]$NodeVersion
)

$ErrorActionPreference = 'Stop'
$windowsRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $windowsRoot
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot "release\Codex-Dream-Skin-Setup-Windows-x64-v$Version.exe" }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "codex-dream-skin-installer-$PID-$([guid]::NewGuid().ToString('N'))"
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
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'skins\rose-garden') -DestinationRoot (Join-Path $engine 'assets\builtin\rose-garden') -Names @('skin.json', 'dream-skin.css', 'art.png')
  foreach ($skinId in @('violet-riviera', 'lilac-salon')) {
    Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot "skins\$skinId") -DestinationRoot (Join-Path $engine "bundled-skins\$skinId") -Names @('skin.json', 'dream-skin.css', 'art.png', 'preview.png')
  }
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'scripts') -DestinationRoot (Join-Path $engine 'scripts') -Names @(
    'common-windows.ps1', 'config-utf8.ps1', 'injector.mjs', 'install-and-start-dream-skin.ps1',
    'install-dream-skin.ps1', 'restore-dream-skin.ps1', 'start-dream-skin.ps1', 'verify-dream-skin.ps1',
    'dream-skin-powershell.cmd'
  )
  Copy-Item -LiteralPath (Join-Path $windowsRoot 'scripts\theme-v2') -Destination (Join-Path $engine 'scripts\theme-v2') -Recurse -Force
  Copy-ReleaseFiles -SourceRoot $nodeSourceRoot -DestinationRoot (Join-Path $engine 'runtime\node') -Names @('node.exe', 'LICENSE', 'README.md')
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'runtime\webp') -DestinationRoot (Join-Path $engine 'runtime\webp') -Names @('dwebp.exe', 'LICENSE.libwebp.txt', 'README.txt')
  $dwebpHash = (Get-FileHash -LiteralPath (Join-Path $engine 'runtime\webp\dwebp.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($dwebpHash -cne 'ee66951df0f868f0c41f49fcc2d0fc53072912b7357836317ca177cbae5eb343') { throw 'Bundled dwebp.exe failed SHA-256 validation.' }
  [System.IO.File]::WriteAllText((Join-Path $payloadRoot 'VERSION'), "$Version`r`n", $utf8NoBom)
  [System.IO.File]::WriteAllText((Join-Path $payloadRoot 'README.txt'), "Codex皮肤主题管理器 $Version`r`n`r`n支持原生 schema v1 与 awesome-codex-skins schema v2 皮肤包。打开 Codex皮肤主题管理器.exe 即可导入、预览、重命名、导出和应用皮肤。导入内容保存在安装目录的 skin 文件夹中。`r`n", $utf8NoBom)

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
      'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'Microsoft.CSharp.dll'
    )

  $testParent = Join-Path $temporaryRoot 'selected-install-location'
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
  if ($active.skinId -cne 'rose-garden') { throw 'Installer smoke test selected the wrong default skin.' }
  foreach ($skinId in @('rose-garden', 'violet-riviera', 'lilac-salon')) {
    $manifestPath = Join-Path $testRoot "skin\$skinId\skin.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Installer smoke test did not seed $skinId" }
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
    if (-not $retainedBackup.FullName.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing to clean an installer test backup outside the temporary root: $($retainedBackup.FullName)"
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
  if (Test-Path -LiteralPath $temporaryRoot) { [System.IO.Directory]::Delete($temporaryRoot, $true) }
}

[CmdletBinding()]
param(
  [string]$OutputPath,
  [string]$Version = '2.0.3',
  [string]$NodeVersion
)

$ErrorActionPreference = 'Stop'
$windowsRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $windowsRoot
$defaultPackageName = "Codex-Dream-Skin-Windows-x64-v$Version"
if (-not $OutputPath) {
  $packageName = $defaultPackageName
  $OutputPath = Join-Path $repoRoot "release\$packageName.zip"
} else {
  $packageName = [System.IO.Path]::GetFileNameWithoutExtension($OutputPath)
  if (-not $packageName) { $packageName = $defaultPackageName }
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "codex-dream-skin-release-$PID-$([guid]::NewGuid().ToString('N'))"
$packageRoot = Join-Path $temporaryRoot $packageName
$engineRoot = Join-Path $packageRoot '.codex-dream-skin'

function Copy-ReleaseFiles {
  param([string]$SourceRoot, [string]$DestinationRoot, [string[]]$Names)
  New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null
  foreach ($name in $Names) {
    Copy-Item -LiteralPath (Join-Path $SourceRoot $name) -Destination (Join-Path $DestinationRoot $name) -Force
  }
}

try {
  [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
  $hostExecutable = (Get-Process -Id $PID -ErrorAction Stop).Path
  & $hostExecutable -NoProfile -File (Join-Path $windowsRoot 'tests\run-tests.ps1')
  if ($LASTEXITCODE -ne 0) { throw "Windows regression tests failed with exit code $LASTEXITCODE" }

  if (-not $NodeVersion) {
    $node = Get-Command node.exe -ErrorAction SilentlyContinue
    if (-not $node) { $node = Get-Command node -ErrorAction Stop }
    $NodeVersion = "$(& $node.Source -p 'process.versions.node')".Trim()
  }
  if ($NodeVersion -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid Node.js version: $NodeVersion" }
  $nodeArchiveName = "node-v$NodeVersion-win-x64.zip"
  $nodeBaseUrl = "https://nodejs.org/dist/v$NodeVersion"
  $nodeArchivePath = Join-Path $temporaryRoot $nodeArchiveName
  $checksumsPath = Join-Path $temporaryRoot 'SHASUMS256.txt'
  Invoke-WebRequest -Uri "$nodeBaseUrl/$nodeArchiveName" -OutFile $nodeArchivePath
  Invoke-WebRequest -Uri "$nodeBaseUrl/SHASUMS256.txt" -OutFile $checksumsPath
  $checksumLine = Get-Content -LiteralPath $checksumsPath | Where-Object { $_ -match "\s$([regex]::Escape($nodeArchiveName))$" } | Select-Object -First 1
  if (-not $checksumLine -or $checksumLine -notmatch '^([0-9a-fA-F]{64})\s+') {
    throw "Official checksum was not found for $nodeArchiveName"
  }
  $expectedNodeHash = $Matches[1].ToLowerInvariant()
  $actualNodeHash = (Get-FileHash -LiteralPath $nodeArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualNodeHash -cne $expectedNodeHash) { throw 'Downloaded Node.js archive failed SHA-256 validation.' }

  $nodeExtractRoot = Join-Path $temporaryRoot 'node-extracted'
  Expand-Archive -LiteralPath $nodeArchivePath -DestinationPath $nodeExtractRoot
  $nodeSourceRoot = Join-Path $nodeExtractRoot "node-v$NodeVersion-win-x64"

  New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
  Copy-Item -Path (Join-Path $windowsRoot 'client-delivery\*') -Destination $packageRoot -Recurse -Force
  & $hostExecutable -NoProfile -File (Join-Path $windowsRoot 'manager\build-manager.ps1') `
    -OutputPath (Join-Path $packageRoot 'Codex皮肤主题管理器.exe')
  if ($LASTEXITCODE -ne 0) { throw "Skin Manager build failed with exit code $LASTEXITCODE" }
  Copy-Item -LiteralPath (Join-Path $repoRoot 'macos\LICENSE') -Destination (Join-Path $packageRoot 'LICENSE')
  $builtInSourceRoot = Join-Path $windowsRoot 'skins\rose-garden'
  Copy-Item -LiteralPath (Join-Path $builtInSourceRoot 'art.png') -Destination (Join-Path $packageRoot 'theme-preview.png')

  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'assets') -DestinationRoot (Join-Path $engineRoot 'assets') -Names @(
    'renderer-inject.js'
  )
  Copy-ReleaseFiles -SourceRoot $builtInSourceRoot -DestinationRoot (Join-Path $engineRoot 'assets\builtin\rose-garden') -Names @(
    'skin.json', 'dream-skin.css', 'art.png'
  )
  foreach ($skinId in @('coral-haze', 'violet-riviera', 'lilac-salon')) {
    Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot "skins\$skinId") -DestinationRoot (Join-Path $engineRoot "bundled-skins\$skinId") -Names @(
      'skin.json', 'dream-skin.css', 'art.png', 'preview.png'
    )
  }
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'scripts') -DestinationRoot (Join-Path $engineRoot 'scripts') -Names @(
    'common-windows.ps1', 'config-utf8.ps1', 'injector.mjs', 'install-and-start-dream-skin.ps1',
    'install-dream-skin.ps1', 'restore-dream-skin.ps1', 'start-dream-skin.ps1', 'verify-dream-skin.ps1',
    'dream-skin-powershell.cmd'
  )
  Copy-Item -LiteralPath (Join-Path $windowsRoot 'scripts\theme-v2') -Destination (Join-Path $engineRoot 'scripts\theme-v2') -Recurse -Force
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'runtime\webp') -DestinationRoot (Join-Path $engineRoot 'runtime\webp') -Names @('dwebp.exe', 'LICENSE.libwebp.txt', 'README.txt')
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'tests') -DestinationRoot (Join-Path $engineRoot 'tests') -Names @('run-tests.ps1')
  Copy-ReleaseFiles -SourceRoot (Join-Path $windowsRoot 'references') -DestinationRoot (Join-Path $engineRoot 'references') -Names @(
    'qa-inventory.md', 'runtime-notes.md'
  )
  Copy-ReleaseFiles -SourceRoot $nodeSourceRoot -DestinationRoot (Join-Path $engineRoot 'runtime\node') -Names @(
    'node.exe', 'LICENSE', 'README.md'
  )

  $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
  [System.IO.File]::WriteAllText((Join-Path $packageRoot 'VERSION'), "$Version`r`n", $utf8NoBom)
  [System.IO.File]::WriteAllText((Join-Path $engineRoot 'runtime\node\ARCHIVE-SHA256.txt'),
    "$expectedNodeHash  $nodeArchiveName`r`n", $utf8NoBom)

  $outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
  [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
  if ([System.IO.File]::Exists($OutputPath)) { [System.IO.File]::Delete($OutputPath) }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [System.IO.Compression.ZipFile]::CreateFromDirectory(
    $packageRoot,
    $OutputPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true
  )
  $releaseHash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
  $hashPath = "$OutputPath.sha256"
  [System.IO.File]::WriteAllText($hashPath, "$releaseHash  $([System.IO.Path]::GetFileName($OutputPath))`r`n", $utf8NoBom)
  Write-Host "Created: $OutputPath"
  Write-Host "SHA-256: $releaseHash"
  Write-Host "Node.js: $NodeVersion ($expectedNodeHash)"
} finally {
  if ([System.IO.Directory]::Exists($temporaryRoot)) {
    [System.IO.Directory]::Delete($temporaryRoot, $true)
  }
}

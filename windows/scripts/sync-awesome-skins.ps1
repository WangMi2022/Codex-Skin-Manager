[CmdletBinding()]
param(
  [string]$ReleaseTag = 'skins-v1.1.0',
  [string]$DestinationRoot,
  [string]$PackageCache
)

$ErrorActionPreference = 'Stop'
$windowsRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $windowsRoot
if (-not $DestinationRoot) { $DestinationRoot = Join-Path $windowsRoot 'skins' }
if (-not $PackageCache) { $PackageCache = Join-Path $repoRoot ".codex_tmp\awesome-release-$($ReleaseTag -replace '[^a-zA-Z0-9._-]', '-')" }
$DestinationRoot = [System.IO.Path]::GetFullPath($DestinationRoot)
$PackageCache = [System.IO.Path]::GetFullPath($PackageCache)
$stageRoot = Join-Path (Join-Path $repoRoot '.codex_tmp') "awesome-sync-$([guid]::NewGuid().ToString('N'))"
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$localSkinIds = @('rose-garden', 'violet-riviera', 'lilac-salon')
$catalogPath = Join-Path $DestinationRoot 'bundled-skins.json'

function Assert-ChildPath {
  param([string]$Parent, [string]$Child)
  $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
  $childFull = [System.IO.Path]::GetFullPath($Child)
  if (-not $childFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Path escapes the managed root: $childFull"
  }
  return $childFull
}

function Read-ZipEntryUtf8 {
  param([System.IO.Compression.ZipArchiveEntry]$Entry)
  $reader = [System.IO.StreamReader]::new($Entry.Open(), [System.Text.UTF8Encoding]::new($false, $true))
  try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null
[System.IO.Directory]::CreateDirectory($PackageCache) | Out-Null
[System.IO.Directory]::CreateDirectory($stageRoot) | Out-Null

try {
  $apiUrl = "https://api.github.com/repos/Wangnov/awesome-codex-skins/releases/tags/$ReleaseTag"
  $headers = @{ 'User-Agent' = 'Codex-Skin-Manager-sync' }
  $release = Invoke-RestMethod -Headers $headers -Uri $apiUrl
  $assets = @($release.assets | Where-Object { $_.name -like '*.codexskin' } | Sort-Object name)
  if ($assets.Count -eq 0) { throw "Release contains no .codexskin assets: $ReleaseTag" }

  $catalogEntries = [System.Collections.Generic.List[object]]::new()
  foreach ($localSkinId in $localSkinIds) {
    $localRoot = Join-Path $DestinationRoot $localSkinId
    if (-not (Test-Path -LiteralPath $localRoot -PathType Container)) { throw "Local bundled skin is missing: $localSkinId" }
    $catalogEntries.Add([ordered]@{ id = $localSkinId; source = 'local' })
  }

  $seenIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($asset in $assets) {
    if ($asset.size -le 0 -or $asset.size -gt 50MB) { throw "Package size is invalid: $($asset.name)" }
    $packagePath = Join-Path $PackageCache $asset.name
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf) -or (Get-Item -LiteralPath $packagePath).Length -ne $asset.size) {
      Invoke-WebRequest -Headers $headers -Uri $asset.browser_download_url -OutFile $packagePath
    }
    if ((Get-Item -LiteralPath $packagePath).Length -ne $asset.size) { throw "Downloaded size mismatch: $($asset.name)" }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
      if ($archive.Entries.Count -eq 0 -or $archive.Entries.Count -gt 500) { throw "Archive entry count is invalid: $($asset.name)" }
      $manifestEntries = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq 'theme.json' })
      if ($manifestEntries.Count -ne 1) { throw "Package must contain one root theme.json: $($asset.name)" }
      $manifest = Read-ZipEntryUtf8 -Entry $manifestEntries[0] | ConvertFrom-Json
      $skinId = [string]$manifest.id
      if ($manifest.schemaVersion -ne 2 -or $skinId -notmatch '^[a-z0-9][a-z0-9-]{0,63}$') {
        throw "Package has an invalid schema or id: $($asset.name)"
      }
      if ($localSkinIds -contains $skinId -or -not $seenIds.Add($skinId)) { throw "Duplicate bundled skin id: $skinId" }

      $skinStage = Assert-ChildPath -Parent $stageRoot -Child (Join-Path $stageRoot $skinId)
      [System.IO.Directory]::CreateDirectory($skinStage) | Out-Null
      $entryNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
      [long]$expandedBytes = 0
      foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if ($name.StartsWith('/', [System.StringComparison]::Ordinal) -or $name.Contains(':') -or
          @($name.Split('/') | Where-Object { $_ -eq '.' -or $_ -eq '..' }).Count -gt 0 -or -not $entryNames.Add($name)) {
          throw "Package contains an unsafe or duplicate path: $($asset.name): $name"
        }
        if ([string]::IsNullOrEmpty($entry.Name)) { continue }
        if ($entry.Length -le 0 -or $entry.Length -gt 24MB) { throw "Package entry size is invalid: $($asset.name): $name" }
        $expandedBytes += $entry.Length
        if ($expandedBytes -gt 96MB) { throw "Expanded package is too large: $($asset.name)" }
        $target = Assert-ChildPath -Parent $skinStage -Child (Join-Path $skinStage $name.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
        $input = $entry.Open()
        $output = [System.IO.File]::Create($target)
        try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
      }

      $cssRelative = if ([string]::IsNullOrWhiteSpace([string]$manifest.css)) { 'theme.css' } else { [string]$manifest.css }
      $cssPath = Assert-ChildPath -Parent $skinStage -Child (Join-Path $skinStage $cssRelative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
      if (-not (Test-Path -LiteralPath $cssPath -PathType Leaf)) { throw "Package CSS is missing: $($asset.name)" }
      $catalogEntries.Add([ordered]@{
        id = $skinId
        source = 'awesome-codex-skins'
        version = [string]$manifest.version
        package = [string]$asset.name
        sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
      })
    } finally {
      $archive.Dispose()
    }
  }

  $previousUpstreamIds = @()
  if (Test-Path -LiteralPath $catalogPath -PathType Leaf) {
    $previousCatalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json
    $previousUpstreamIds = @($previousCatalog.skins | Where-Object { $_.source -eq 'awesome-codex-skins' } | ForEach-Object { [string]$_.id })
  }

  foreach ($entry in @($catalogEntries | Where-Object { $_.source -eq 'awesome-codex-skins' })) {
    $skinId = [string]$entry.id
    $source = Assert-ChildPath -Parent $stageRoot -Child (Join-Path $stageRoot $skinId)
    $destination = Assert-ChildPath -Parent $DestinationRoot -Child (Join-Path $DestinationRoot $skinId)
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    Move-Item -LiteralPath $source -Destination $destination
  }

  $currentUpstreamIds = @($catalogEntries | Where-Object { $_.source -eq 'awesome-codex-skins' } | ForEach-Object { [string]$_.id })
  foreach ($obsoleteId in $previousUpstreamIds) {
    if ($currentUpstreamIds -contains $obsoleteId) { continue }
    $obsoletePath = Assert-ChildPath -Parent $DestinationRoot -Child (Join-Path $DestinationRoot $obsoleteId)
    if (Test-Path -LiteralPath $obsoletePath) { Remove-Item -LiteralPath $obsoletePath -Recurse -Force }
  }

  $catalog = [ordered]@{
    schemaVersion = 1
    source = 'https://github.com/Wangnov/awesome-codex-skins'
    releaseTag = [string]$release.tag_name
    publishedAt = ([datetime]$release.published_at).ToUniversalTime().ToString('o')
    skins = @($catalogEntries)
  }
  [System.IO.File]::WriteAllText($catalogPath, (($catalog | ConvertTo-Json -Depth 5) + "`n"), $utf8NoBom)
  Write-Host "Synced $($assets.Count) awesome-codex-skins packages; bundled total: $($catalogEntries.Count)."
  Write-Host "Catalog: $catalogPath"
} finally {
  $resolvedStage = [System.IO.Path]::GetFullPath($stageRoot)
  $expectedParent = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '.codex_tmp')).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
  if ($resolvedStage.StartsWith($expectedParent, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedStage)) {
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
  }
}

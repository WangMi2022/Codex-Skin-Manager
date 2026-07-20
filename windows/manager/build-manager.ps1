[CmdletBinding()]
param(
  [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$managerRoot = $PSScriptRoot
$windowsRoot = Split-Path -Parent $managerRoot
if (-not $OutputPath) { $OutputPath = Join-Path $managerRoot 'bin\Codex皮肤主题管理器.exe' }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$compilerCandidates = @(
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'Windows .NET Framework C# compiler was not found.' }

$iconSource = Join-Path $managerRoot 'dream-skin-manager.ico'
if (-not (Test-Path -LiteralPath $iconSource -PathType Leaf)) {
  throw "Skin Manager icon was not found: $iconSource"
}
$iconPath = Join-Path $outputDirectory 'dream-skin-manager.ico'
if ([System.StringComparer]::OrdinalIgnoreCase.Compare($iconSource, $iconPath) -ne 0) {
  Copy-Item -LiteralPath $iconSource -Destination $iconPath -Force
}

$sources = Get-ChildItem -LiteralPath $managerRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
$references = @(
  'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
  'System.Web.Extensions.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll'
)
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/debug-', '/langversion:5',
  "/out:$OutputPath", "/win32icon:$iconPath", "/win32manifest:$(Join-Path $managerRoot 'app.manifest')")
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $OutputPath)) {
  throw "Skin Manager compilation failed with exit code $LASTEXITCODE"
}

$selfTest = Start-Process -FilePath $OutputPath -ArgumentList @('--self-test', $windowsRoot) -PassThru -Wait
if ($selfTest.ExitCode -ne 0) { throw "Skin Manager self-test failed with exit code $($selfTest.ExitCode)" }
Write-Host "Created: $OutputPath"

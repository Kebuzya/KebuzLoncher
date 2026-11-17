param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [string]$MSBuildPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if (-not $MSBuildPath) { $MSBuildPath = (Get-Command msbuild -ErrorAction Stop).Source }
$compilerDirectory = Split-Path -Parent $MSBuildPath
$compiler = Join-Path $compilerDirectory 'Roslyn/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path (Split-Path -Parent $compilerDirectory) 'Roslyn/csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The MSBuild Roslyn compiler was not found.' }
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('KebuzLoncher-package-' + [Guid]::NewGuid().ToString('N'))))
if (-not $testDirectory.StartsWith($temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package test directory.' }
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$launcher = Join-Path $testDirectory 'KebuzLoncher.exe'
$checker = Join-Path $testDirectory 'PackagingChecks.exe'
try {
    Copy-Item -LiteralPath $executable -Destination $launcher
    $jsonAssembly = Join-Path $projectRoot '../packages/Newtonsoft.Json.13.0.4/lib/net45/Newtonsoft.Json.dll'
    & $compiler /nologo /target:exe "/out:$checker" "/reference:$launcher" "/reference:$jsonAssembly" /reference:System.Drawing.dll (Join-Path $projectRoot 'tests/PackagingChecks.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Package checks did not compile.' }
    & $checker
    if ($LASTEXITCODE -ne 0) { throw 'Single EXE package verification failed.' }
    Write-Output ('SHA-256: ' + (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant())
}
finally {
    foreach ($file in @($checker, $launcher)) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force } }
    if (Test-Path -LiteralPath $testDirectory) { Remove-Item -LiteralPath $testDirectory }
}

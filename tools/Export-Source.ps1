param()
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDir = Join-Path (Join-Path (Split-Path -Parent $projectRoot) '.artifacts') 'KebuzLoncher'
[IO.Directory]::CreateDirectory($outputDir) | Out-Null
$archivePath = Join-Path $outputDir 'KebuzLoncher-source.zip'

# Explicit allow-list; never recurse over the parent workspace or bin/obj.
$relativeFiles = @(
    '.gitignore', 'LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md',
    'config.example.json', 'manifest.example.json', 'RELEASING.md', 'KebuzLoncher.csproj', 'packages.config',
    'App.config', 'app.manifest', 'KebuzLoncher.ico', 'FodyWeavers.xml', 'FodyWeavers.xsd', 'Form1.resx'
    'docs/DEVELOPMENT.md', 'docs/images/dark-theme.png'
)
$relativeFiles += Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -File | ForEach-Object { $_.Name }
foreach ($folder in @('Properties', 'licenses', 'tools', 'tests')) {
    $folderPath = Join-Path $projectRoot $folder
    if (Test-Path -LiteralPath $folderPath) {
        $relativeFiles += Get-ChildItem -LiteralPath $folderPath -File |
            Where-Object { $_.Extension -in @('.cs', '.resx', '.settings', '.txt', '.md', '.ps1') } |
            ForEach-Object { "$folder/$($_.Name)" }
    }
}

$relativeFiles = $relativeFiles | Where-Object {
    [IO.Path]::GetFileName($_) -notlike 'AGENTS*.md' -and
    [IO.Path]::GetFileName($_) -notin @('SKILL.md', 'CLAUDE.md', 'GEMINI.md')
}

foreach ($relative in $relativeFiles) {
    $path = Join-Path $projectRoot $relative
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing source file: $relative" }
    if ([IO.Path]::GetExtension($path) -in @('.png', '.ico')) { continue }
    $content = [IO.File]::ReadAllText($path)
    if ($content -match '(?i)gh[pousr]_[a-z0-9]{30,}|github_pat_[a-z0-9_]{30,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----') {
        throw "Possible credential in $relative; archive was not created."
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($archivePath, [IO.FileMode]::Create)
try {
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($relative in ($relativeFiles | Sort-Object -Unique)) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, (Join-Path $projectRoot $relative), "KebuzLoncher/$relative") | Out-Null
        }
    }
    finally { $zip.Dispose() }
}
finally { $stream.Dispose() }
Write-Host "Source archive: $archivePath"

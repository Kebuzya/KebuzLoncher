param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^launcher-v([0-9]+\.[0-9]+\.[0-9]+)$') { throw 'Launcher tags must use launcher-vMAJOR.MINOR.PATCH.' }
$version = $Matches[1]
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid GitHub repository.' }
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
if ($info.ProductVersion -ne $version) { throw 'The launcher tag does not match the built EXE version.' }
$checksum = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
if ($DryRun) { Write-Output "Validated $Tag for $Repository. EXE SHA-256: $checksum"; return }
if (-not $env:GH_TOKEN) { throw 'GH_TOKEN is required for publishing.' }
$apiRoot = "https://api.github.com/repos/$Repository"
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'KebuzLoncher-Release'; 'X-GitHub-Api-Version' = '2022-11-28' }

function Invoke-GitHub([string]$Method, [string]$Path, $Payload, [switch]$AllowMissing) {
    $request = @{ Uri = $apiRoot + $Path; Method = $Method; Headers = $headers; TimeoutSec = 30; ErrorAction = 'Stop' }
    if ($null -ne $Payload) { $request.Body = [Text.Encoding]::UTF8.GetBytes(($Payload | ConvertTo-Json -Depth 32 -Compress)); $request.ContentType = 'application/json' }
    try { Invoke-RestMethod @request }
    catch { if ($AllowMissing -and [int]$_.Exception.Response.StatusCode -eq 404) { return $null }; throw }
}

function Read-Manifest {
    $file = Invoke-GitHub GET '/contents/manifest.json?ref=catalog' $null
    $manifest = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($file.content)) | ConvertFrom-Json
    $current = [version]$manifest.launcher_version
    $normalized = [version]('{0}.{1}.{2}.{3}' -f $current.Major, $current.Minor, [Math]::Max(0, $current.Build), [Math]::Max(0, $current.Revision))
    if ($normalized -gt [version]"$version.0") { throw 'Refusing to replace a newer launcher manifest.' }
    return @{ File = $file; Content = $manifest }
}

$null = Read-Manifest
$release = Invoke-GitHub GET "/releases/tags/$Tag" $null -AllowMissing
if ($null -eq $release) {
    $release = Invoke-GitHub POST '/releases' @{ tag_name = $Tag; name = "Kebuz Launcher $version"; draft = $true; prerelease = $false; make_latest = 'false'; generate_release_notes = $true }
}
if ($release.prerelease) { throw 'A stable launcher cannot replace a prerelease.' }
if (@($release.assets | Where-Object { $_.name -ne 'KebuzLoncher.exe' }).Count -gt 0) { throw 'The launcher release contains unexpected assets.' }
$asset = $release.assets | Where-Object { $_.name -eq 'KebuzLoncher.exe' } | Select-Object -First 1
if ($null -ne $asset -and ($asset.state -ne 'uploaded' -or $asset.digest -ne "sha256:$checksum")) {
    if (-not $release.draft) { throw 'Refusing to overwrite a published EXE with different contents.' }
    $null = Invoke-GitHub DELETE "/releases/assets/$($asset.id)" $null
    $asset = $null
}
if ($null -eq $asset) {
    if (-not $release.draft) { throw 'A published launcher release has no verified EXE.' }
    $upload = ($release.upload_url -split '\{')[0] + '?name=KebuzLoncher.exe'
    if (([uri]$upload).Host -ne 'uploads.github.com') { throw 'Unexpected upload host.' }
    $asset = Invoke-RestMethod -Uri $upload -Method POST -Headers $headers -InFile $executable -ContentType 'application/octet-stream' -TimeoutSec 120 -ErrorAction Stop
}
if ($asset.digest -ne "sha256:$checksum") { throw 'The uploaded asset checksum does not match the EXE.' }
if ($release.draft) { $release = Invoke-GitHub PATCH "/releases/$($release.id)" @{ draft = $false; make_latest = 'true' } }
$asset = Invoke-GitHub GET "/releases/assets/$($asset.id)" $null
if ($asset.name -ne 'KebuzLoncher.exe' -or $asset.state -ne 'uploaded' -or $asset.digest -ne "sha256:$checksum") { throw 'The published asset no longer matches the verified EXE.' }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('KebuzLoncher-download-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    if (([uri]$asset.browser_download_url).Scheme -ne 'https') { throw 'The public asset must use HTTPS.' }
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $temporary -UseBasicParsing -TimeoutSec 120 -ErrorAction Stop
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $checksum) { throw 'The public download failed checksum verification.' }
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }

$downloadUrl = "https://github.com/$Repository/releases/tag/$Tag"
for ($attempt = 0; $attempt -lt 3; $attempt++) {
    $snapshot = Read-Manifest
    $manifest = $snapshot.Content
    if ($manifest.launcher_version -eq $version -and $manifest.launcher_download_url -eq $downloadUrl) { Write-Output 'The launcher manifest is already current.'; return }
    $manifest | Add-Member -NotePropertyName launcher_version -NotePropertyValue $version -Force
    $manifest | Add-Member -NotePropertyName launcher_download_url -NotePropertyValue $downloadUrl -Force
    $content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 32)))
    try {
        $null = Invoke-GitHub PUT '/contents/manifest.json' @{ branch = 'catalog'; sha = $snapshot.File.sha; message = "Release launcher $version"; content = $content }
        Write-Output "Published $Tag and updated catalog/manifest.json."
        return
    }
    catch { if ([int]$_.Exception.Response.StatusCode -ne 409 -or $attempt -eq 2) { throw } }
}

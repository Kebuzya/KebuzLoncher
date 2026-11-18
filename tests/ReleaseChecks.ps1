param([Parameter(Mandatory = $true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$publishScript = Join-Path $PSScriptRoot '../tools/Publish-Launcher.ps1'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
$tag = "launcher-v$version"
$checksum = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
$arguments = @{ Tag = $tag; Repository = 'owner/repo'; ExecutablePath = $executable }
$originalToken = $env:GH_TOKEN
$checks = 0

Add-Type -TypeDefinition @'
public sealed class LauncherReleaseTestResponse : System.Net.WebResponse
{
    public System.Net.HttpStatusCode StatusCode { get; private set; }
    public LauncherReleaseTestResponse(System.Net.HttpStatusCode status) { StatusCode = status; }
}
'@

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Reset-Fixture([string]$Current = '0.0.1') {
    $global:LauncherReleaseFixture = @{
        Manifest = [pscustomobject]@{ launcher_version = $Current; launcher_download_url = "https://github.com/owner/repo/releases/tag/launcher-v$Current"; keep = 'preserved' }
        Revision = 1; Release = $null; Calls = [Collections.Generic.List[string]]::new(); Conflicts = 0; CorruptDownload = $false
    }
}

function New-FixtureError([int]$Status) {
    return [Net.WebException]::new('Fixture HTTP failure', $null, [Net.WebExceptionStatus]::ProtocolError, [LauncherReleaseTestResponse]::new([Net.HttpStatusCode]$Status))
}

function Invoke-RestMethod {
    [CmdletBinding()]
    param([string]$Uri, [string]$Method, $Headers, $Body, [string]$ContentType, [string]$InFile, [int]$TimeoutSec)
    $state = $global:LauncherReleaseFixture
    $path = ([uri]$Uri).AbsolutePath
    $state.Calls.Add("$Method $path")
    if ($Headers.Authorization -ne 'Bearer release-test-token') { throw 'Release checks must never use real credentials.' }
    $payload = if ($null -ne $Body) { [Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json } else { $null }
    if ($Method -eq 'GET' -and $path.EndsWith('/contents/manifest.json')) {
        return [pscustomobject]@{ sha = "manifest-$($state.Revision)"; content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($state.Manifest | ConvertTo-Json))) }
    }
    if ($Method -eq 'GET' -and $path.Contains('/releases/tags/')) {
        if ($null -eq $state.Release) { throw (New-FixtureError 404) }
        return $state.Release
    }
    if ($Method -eq 'POST' -and $path -eq '/repos/owner/repo/releases') {
        if (-not $payload.draft -or $payload.make_latest -ne 'false') { throw 'New releases must remain drafts while their EXE is uploaded.' }
        $state.Release = [pscustomobject]@{ id = 123; draft = $true; prerelease = $false; assets = @(); upload_url = 'https://uploads.github.com/repos/owner/repo/releases/123/assets{?name,label}' }
        return $state.Release
    }
    if ($Method -eq 'POST' -and $path.EndsWith('/releases/123/assets')) {
        if (-not $state.Release.draft -or (Get-FileHash -LiteralPath $InFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $checksum) { throw 'Invalid EXE upload.' }
        $asset = [pscustomobject]@{ id = 456; name = 'KebuzLoncher.exe'; state = 'uploaded'; digest = "sha256:$checksum"; browser_download_url = 'https://github.com/owner/repo/releases/download/untagged-fixture/KebuzLoncher.exe' }
        $state.Release.assets = @($asset)
        return $asset
    }
    if ($Method -eq 'DELETE' -and $path.EndsWith('/releases/assets/456')) {
        if (-not $state.Release.draft) { throw 'Published assets must never be deleted.' }
        $state.Release.assets = @()
        return
    }
    if ($Method -eq 'GET' -and $path.EndsWith('/releases/assets/456')) {
        if ($state.Release.draft) { throw 'Asset metadata must be refreshed after publication.' }
        $asset = $state.Release.assets[0] | Select-Object *
        $asset.browser_download_url = "https://github.com/owner/repo/releases/download/$tag/KebuzLoncher.exe"
        return $asset
    }
    if ($Method -eq 'PATCH' -and $path.EndsWith('/releases/123')) {
        if ($payload.draft -or $payload.make_latest -ne 'true') { throw 'The verified launcher must become a public Latest release.' }
        $state.Release.draft = $false
        return $state.Release
    }
    if ($Method -eq 'PUT' -and $path.EndsWith('/contents/manifest.json')) {
        if ($state.Conflicts -gt 0) { $state.Conflicts--; $state.Revision++; throw (New-FixtureError 409) }
        if ($payload.branch -ne 'catalog' -or $payload.sha -ne "manifest-$($state.Revision)") { throw 'Manifest writes must use the current catalog revision.' }
        $state.Manifest = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload.content)) | ConvertFrom-Json
        $state.Revision++
        return [pscustomobject]@{ commit = [pscustomobject]@{ sha = 'fixture-commit' } }
    }
    throw "Unexpected publishing request: $Method $path"
}

function Invoke-WebRequest {
    [CmdletBinding()]
    param([string]$Uri, [string]$OutFile, [switch]$UseBasicParsing, [int]$TimeoutSec)
    $state = $global:LauncherReleaseFixture
    $state.Calls.Add('DOWNLOAD')
    if ($state.Release.draft) { throw 'Public verification must follow publication.' }
    if ($Uri -ne "https://github.com/owner/repo/releases/download/$tag/KebuzLoncher.exe") { throw 'Public verification must use refreshed asset metadata instead of the temporary draft URL.' }
    if ($state.CorruptDownload) { [IO.File]::WriteAllText($OutFile, 'corrupted') }
    else { Copy-Item -LiteralPath $executable -Destination $OutFile }
}

function Assert-Failure([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action | Out-Null }
    catch { $failed = $true }
    Assert-Check $failed $Message
}

try {
    Reset-Fixture
    $env:GH_TOKEN = ''
    & $publishScript @arguments -DryRun | Out-Null
    Assert-Check ($global:LauncherReleaseFixture.Calls.Count -eq 0) 'Dry runs must not require credentials or access GitHub.'
    $env:GH_TOKEN = 'release-test-token'
    Assert-Failure { & $publishScript -Tag 'modpack-fixture-latest' -Repository 'owner/repo' -ExecutablePath $executable } 'Modpack tags must never publish the launcher.'
    $otherVersion = ([version]$version).Major + 1
    Assert-Failure { & $publishScript -Tag "launcher-v$otherVersion.0.0" -Repository 'owner/repo' -ExecutablePath $executable } 'Tags must match the embedded EXE version.'
    Assert-Check ($global:LauncherReleaseFixture.Calls.Count -eq 0) 'Invalid tags must fail before remote access.'

    & $publishScript @arguments | Out-Null
    $state = $global:LauncherReleaseFixture
    Assert-Check ($state.Manifest.launcher_version -eq $version -and $state.Manifest.keep -eq 'preserved') 'Publication must update the version and preserve unknown manifest fields.'
    Assert-Check ($state.Calls.IndexOf('DOWNLOAD') -lt $state.Calls.IndexOf('PUT /repos/owner/repo/contents/manifest.json')) 'The manifest must be updated only after public download verification.'
    Assert-Check ($state.Calls.IndexOf('GET /repos/owner/repo/releases/assets/456') -gt $state.Calls.IndexOf('PATCH /repos/owner/repo/releases/123') -and $state.Calls.IndexOf('GET /repos/owner/repo/releases/assets/456') -lt $state.Calls.IndexOf('DOWNLOAD')) 'The published download URL must be refreshed after the draft becomes public.'
    Assert-Check (-not $state.Release.draft -and $state.Release.assets.Count -eq 1) 'A published launcher must contain exactly its EXE.'
    $state.Calls.Clear()
    & $publishScript @arguments | Out-Null
    Assert-Check (@($state.Calls | Where-Object { $_ -match '^(POST|PUT|PATCH|DELETE) ' }).Count -eq 0) 'A successful rerun must not rewrite published assets or the manifest.'

    $state.Release.assets[0].digest = 'sha256:different'
    $state.Calls.Clear()
    Assert-Failure { & $publishScript @arguments } 'Published EXEs must not be overwritten with different contents.'
    Assert-Check (@($state.Calls | Where-Object { $_ -match '^(POST|PUT|PATCH|DELETE) ' }).Count -eq 0) 'A published asset mismatch must not cause remote writes.'

    Reset-Fixture
    $state = $global:LauncherReleaseFixture
    $state.CorruptDownload = $true
    Assert-Failure { & $publishScript @arguments } 'Corrupted public downloads must abort manifest updates.'
    Assert-Check ($state.Manifest.launcher_version -eq '0.0.1') 'Failed public verification must preserve the previous update feed.'

    Reset-Fixture
    $state = $global:LauncherReleaseFixture
    $state.Conflicts = 1
    & $publishScript @arguments | Out-Null
    Assert-Check ($state.Manifest.launcher_version -eq $version -and @($state.Calls | Where-Object { $_ -eq 'PUT /repos/owner/repo/contents/manifest.json' }).Count -eq 2) 'Concurrent catalog writes must be retried using a fresh manifest revision.'
    Assert-Check (@($state.Calls | Where-Object { $_.Contains('config.json') }).Count -eq 0) 'Launcher releases must never access the game catalog.'

    $newer = '{0}.{1}.{2}' -f ([version]$version).Major, ([version]$version).Minor, (([version]$version).Build + 1)
    Reset-Fixture $newer
    Assert-Failure { & $publishScript @arguments } 'Older releases must never replace a newer update feed.'
    Assert-Check (@($global:LauncherReleaseFixture.Calls | Where-Object { $_ -match '^(POST|PUT|PATCH|DELETE) ' }).Count -eq 0) 'Version rollback must fail before publishing.'
    Write-Output "PASS: $checks release checks (tags, drafts, checksums, retries, idempotence and isolated manifest writes)."
}
finally {
    $env:GH_TOKEN = $originalToken
    Remove-Variable -Name LauncherReleaseFixture -Scope Global -ErrorAction SilentlyContinue
}

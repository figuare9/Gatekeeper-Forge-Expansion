$ErrorActionPreference = 'Stop'
$taskProjectRoot = $PSScriptRoot
$taskSdk = Join-Path $taskProjectRoot 'tools/dotnet/dotnet.exe'
$taskKitSource = Get-Content -Raw -LiteralPath (Join-Path $taskProjectRoot 'src/Manager.Core/ExpansionKit.cs')
$taskKitVersion = [regex]::Match($taskKitSource, 'const string Version = "([^"]+)"').Groups[1].Value
$taskContentVersion = [regex]::Match($taskKitSource, 'const string ContentVersion = "([^"]+)"').Groups[1].Value
if (-not $taskKitVersion -or -not $taskContentVersion) { throw 'Installation kit version metadata is missing.' }
& (Join-Path $taskProjectRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The normal build failed.' }

# Only common support libraries are copied; generated game bindings are excluded.
$taskUnityZip = Join-Path (Split-Path $taskProjectRoot -Parent) 'BepInEx/unity-libs/2022.3.62.zip'
if (-not (Test-Path -LiteralPath $taskUnityZip)) { throw 'The pinned Unity support ZIP is missing.' }
$taskUnityTarget = Join-Path $taskProjectRoot 'packages/loader/BepInEx/unity-libs/2022.3.62.zip'
New-Item -ItemType Directory -Path (Split-Path $taskUnityTarget -Parent) -Force | Out-Null
Copy-Item -LiteralPath $taskUnityZip -Destination $taskUnityTarget -Force

$taskNoticeRoot = Join-Path $taskProjectRoot 'packages/portable-notices'
New-Item -ItemType Directory -Path $taskNoticeRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $taskNoticeRoot 'BepInEx-LICENSE.txt'))) {
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/BepInEx/BepInEx/b782da4/LICENSE' -OutFile (Join-Path $taskNoticeRoot 'BepInEx-LICENSE.txt')
}
foreach ($taskName in @('SHARE_WITH_FRIENDS.md','LICENSE','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $taskProjectRoot $taskName) -Destination (Join-Path $taskNoticeRoot $taskName) -Force
}
Copy-Item -LiteralPath (Join-Path $taskProjectRoot 'tools/dotnet/LICENSE.txt') -Destination (Join-Path $taskNoticeRoot 'DOTNET-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $taskProjectRoot 'tools/dotnet/ThirdPartyNotices.txt') -Destination (Join-Path $taskNoticeRoot 'DOTNET-ThirdPartyNotices.txt') -Force
# Publish exactly the articles referenced by the validated embedded catalog.
# Earlier generated notes must not remain in a later release by accident.
Get-ChildItem -LiteralPath $taskNoticeRoot -Filter 'news-*' -File | Remove-Item
$taskNewsRoot = Join-Path $taskProjectRoot 'content/news'
$taskNewsIndex = Get-Content -Raw -LiteralPath (Join-Path $taskNewsRoot 'catalog.json') | ConvertFrom-Json
Copy-Item -LiteralPath (Join-Path $taskNewsRoot 'catalog.json') -Destination (Join-Path $taskNoticeRoot 'news-catalog.json') -Force
foreach ($taskArticle in $taskNewsIndex.expansions.articles) {
    if ($taskArticle.file -notmatch '^[a-z0-9][a-z0-9-]*\.md$') { throw 'Invalid news article filename.' }
    Copy-Item -LiteralPath (Join-Path $taskNewsRoot $taskArticle.file) -Destination (Join-Path $taskNoticeRoot ('news-' + $taskArticle.file)) -Force
}

Add-Type -AssemblyName System.IO.Compression
$taskPayload = Join-Path $taskProjectRoot 'packages/portable-payload.zip'
$taskItems = [System.Collections.Generic.List[object]]::new()
foreach ($taskFolder in @('packages/loader','packages/mods','packages/portable-notices')) {
    foreach ($taskFile in [System.IO.Directory]::EnumerateFiles((Join-Path $taskProjectRoot $taskFolder),'*',[System.IO.SearchOption]::AllDirectories)) {
        $taskRelative = [System.IO.Path]::GetRelativePath($taskProjectRoot,$taskFile).Replace('\','/')
        if ($taskRelative -match '/(interop|dummy|cache|config|state|backups)/' -or $taskRelative -match '(GameAssembly\.dll|global-metadata\.dat)$') { throw 'Private or game-derived file in payload: ' + $taskRelative }
        if ($taskRelative.StartsWith('packages/portable-notices/')) { $taskRelative = 'notices/' + [System.IO.Path]::GetFileName($taskFile) }
        $taskItems.Add([pscustomobject]@{ Source=$taskFile; Path=$taskRelative; Bytes=([System.IO.FileInfo]::new($taskFile)).Length; Sha256=(Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash.ToLowerInvariant() })
    }
}
$taskManifest = @{ Version=$taskContentVersion; Files=@($taskItems | ForEach-Object { @{Path=$_.Path;Bytes=$_.Bytes;Sha256=$_.Sha256} }) } | ConvertTo-Json -Depth 5
$taskZipStream = [System.IO.File]::Open($taskPayload,[System.IO.FileMode]::Create)
$taskZip = [System.IO.Compression.ZipArchive]::new($taskZipStream,[System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskItem in $taskItems) { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip,$taskItem.Source,$taskItem.Path,[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null }
    $taskManifestEntry = $taskZip.CreateEntry('bundle.json')
    $taskWriter = [System.IO.StreamWriter]::new($taskManifestEntry.Open(),[System.Text.UTF8Encoding]::new($false))
    try { $taskWriter.Write($taskManifest) } finally { $taskWriter.Dispose() }
} finally { $taskZip.Dispose(); $taskZipStream.Dispose() }

& $taskSdk run --project (Join-Path $taskProjectRoot 'src/Manager.Tests/Manager.Tests.csproj') -c Release --no-build -- --payload $taskPayload
if ($LASTEXITCODE -ne 0) { throw 'The real bundled package failed its installation checks.' }

$taskPublish = Join-Path $taskProjectRoot 'portable-app'
& $taskSdk publish (Join-Path $taskProjectRoot 'src/Manager/Manager.csproj') -c Release -p:PortablePayload=true -o $taskPublish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Single-file publish failed.' }
$taskRelease = Join-Path $taskProjectRoot ('release/Gatekeeper.ForgeExpansion-Setup-v' + $taskKitVersion + '.exe')
Copy-Item -LiteralPath (Join-Path $taskPublish 'Gatekeeper.ModManager.exe') -Destination $taskRelease -Force
Write-Output ('Single-file release: ' + $taskRelease)

$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskSdk = Join-Path $taskRoot 'tools/dotnet10/dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskSource = Join-Path $taskRoot 'vendor/BepInEx'
& $taskSdk build (Join-Path $taskSource 'Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj') -c Release '-p:SolutionDir=./' '-p:BuildDir=../../../../build-net10' '-p:RestoreLockedMode=true' --nologo
if ($LASTEXITCODE -ne 0) { throw 'The Windows/Proton loader compatibility build failed.' }
$taskOutput = Join-Path $taskRoot 'vendor/build-net10/Unity.IL2CPP'
Get-ChildItem -LiteralPath $taskOutput -Filter '*.dll' -File | Copy-Item -Destination (Join-Path $taskRoot 'packages/loader/BepInEx/core') -Force
& python -B (Join-Path $taskRoot 'tools/loader_dependency_manifest.py')
if ($LASTEXITCODE -ne 0) { throw 'Loader dependency provenance could not be verified.' }

# Include the complete corresponding loader source and original license as
# ordinary files, not an executable or nested source archive.
$taskDestination = Join-Path $taskRoot 'packages/portable-notices/loader-source'
foreach ($taskFile in Get-ChildItem -LiteralPath $taskSource -Recurse -File) {
    $taskRelative = [IO.Path]::GetRelativePath($taskSource,$taskFile.FullName).Replace('\','/')
    if ($taskRelative -match '(^|/)(bin|obj|\.git|\.vs|\.idea)/') { continue }
    $taskTarget = Join-Path $taskDestination $taskRelative
    New-Item -ItemType Directory -Path (Split-Path $taskTarget -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $taskFile.FullName -Destination $taskTarget -Force
}

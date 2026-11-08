$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sdk = Join-Path $projectRoot 'tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { throw 'Place a .NET 8 SDK in tools\dotnet before building.' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $sdk run --project (Join-Path $projectRoot 'src\Manager.Tests\Manager.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Manager checks failed.' }
& $sdk build (Join-Path $projectRoot 'src\SubtleBalance\SubtleBalance.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
& $sdk build (Join-Path $projectRoot 'src\GameMods\GameMods.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Custom mods build failed. First launch the IL2CPP loader once to generate BepInEx\interop.' }
& $sdk publish (Join-Path $projectRoot 'src\Manager\Manager.csproj') -c Release -o (Join-Path $projectRoot 'app') --no-self-contained --nologo
if ($LASTEXITCODE -ne 0) { throw 'Manager build failed. Close the manager and try again.' }
$pluginPackage = Join-Path $projectRoot 'packages\mods'
New-Item -ItemType Directory -Path $pluginPackage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\SubtleBalance\bin\Release\net6.0\Gatekeeper.SubtleBalance.dll') -Destination (Join-Path $pluginPackage 'Gatekeeper.SubtleBalance.dll') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\GameMods\bin\Release\net6.0\Gatekeeper.CustomMods.dll') -Destination (Join-Path $pluginPackage 'Gatekeeper.CustomMods.dll') -Force
Write-Output 'Build complete. Open Launch Manager.cmd.'

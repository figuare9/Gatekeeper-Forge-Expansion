$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskSdk = Join-Path $taskRoot 'tools/dotnet/dotnet.exe'
$taskPayload = Join-Path $taskRoot 'packages/portable-payload.zip'
$taskVersion = [regex]::Match((Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'src/Manager.Core/ExpansionKit.cs')), 'const string Version = "([^"]+)"').Groups[1].Value
if (-not $taskVersion) { throw 'Installation kit version metadata is missing.' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (-not (Test-Path -LiteralPath $taskPayload)) { throw 'Build the matching Windows portable payload first. This build reuses it without rebuilding or installing gameplay plugins.' }
& $taskSdk run --project (Join-Path $taskRoot 'src/Manager.Tests/Manager.Tests.csproj') -c Release -- --payload $taskPayload
if ($LASTEXITCODE -ne 0) { throw 'Installer or bundled-payload checks failed.' }
$taskPublish = Join-Path $taskRoot 'linux-app'
& $taskSdk publish (Join-Path $taskRoot 'src/Manager.Linux/Manager.Linux.csproj') -c Release -p:LinuxBundle=true -o $taskPublish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Linux installer cross-publish failed.' }
& python -B (Join-Path $taskRoot 'linux/package_linux.py') --root $taskRoot --version $taskVersion
if ($LASTEXITCODE -ne 0) { throw 'Linux distribution packaging failed.' }

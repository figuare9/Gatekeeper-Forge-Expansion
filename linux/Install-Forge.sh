#!/bin/sh
set -eu
task_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
if [ "$(uname -s)" != "Linux" ] || [ "$(uname -m)" != "x86_64" ]; then
    printf '%s\n' 'This installation kit requires x86-64 Linux. Use the Windows installer on Windows.' >&2
    exit 1
fi
if [ "$(id -u)" -eq 0 ]; then
    printf '%s\n' 'Run this installer as your normal Steam user, without sudo.' >&2
    exit 1
fi
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/GatekeeperModManager/runtime"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
exec "$task_dir/Gatekeeper.ForgeExpansion.Linux" "$@"

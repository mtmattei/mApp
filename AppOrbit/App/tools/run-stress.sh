#!/usr/bin/env bash
# Stress-tests the Release build headless under Xvfb against a graph file and writes a log.
#   tools/run-stress.sh <graph.json> <log>      (from AppOrbit/App; needs a Release build)
set -u
cd "$(dirname "$0")/.."
DLL=AppOrbit/bin/Release/net10.0-desktop/AppOrbit.dll
[ -f "$DLL" ] || { echo "build first: dotnet build AppOrbit/AppOrbit.csproj -c Release -f net10.0-desktop"; exit 2; }
GRAPH="$(realpath "$1")"; LOG="$(realpath -m "$2")"
rm -f "$LOG"; rm -rf "$HOME/.local/share/App Orbit"
export APP_NO_HOTDESIGN=1 APP_ORBIT_STRESS=1 APP_ORBIT_EXIT=1 APP_ORBIT_THEME=light APP_ORBIT_GRAPH="$GRAPH" APP_ORBIT_LOG="$LOG"
start=$(date +%s)
xvfb-run -a -s "-screen 0 1440x900x24" timeout "${APP_ORBIT_TIMEOUT:-900}" dotnet "$DLL" > "${LOG%.log}.out" 2>&1
code=$?
echo "exit $code after $(( $(date +%s) - start )) s"
exit $code

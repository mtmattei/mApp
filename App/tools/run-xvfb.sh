#!/usr/bin/env bash
# Runs the app headless under Xvfb at the design viewport, drives the journey runner and captures
# one screenshot per stop. Exit code is the runner's.
#   tools/run-xvfb.sh            (from AppOrbit/App; needs a Debug build)   → shots/
#   tools/run-xvfb.sh dark       (the same journey in the dark theme)       → shots-dark/
set -u
cd "$(dirname "$0")/.."
DLL=AppOrbit/bin/Debug/net10.0-desktop/AppOrbit.dll
[ -f "$DLL" ] || { echo "build first: dotnet build AppOrbit/AppOrbit.csproj"; exit 2; }
THEME="${1:-light}"
OUT="shots"; [ "$THEME" = "dark" ] && OUT="shots-dark"
mkdir -p "$OUT"; rm -f "$OUT/journey.log"
export APP_NO_HOTDESIGN=1 APP_ORBIT_JOURNEY=1 APP_ORBIT_EXIT=1 APP_ORBIT_THEME="$THEME" APP_ORBIT_SHOTS="$PWD/$OUT" APP_ORBIT_LOG="$PWD/$OUT/journey.log"
xvfb-run -a -s "-screen 0 1440x900x24" timeout "${APP_ORBIT_TIMEOUT:-150}" dotnet "$DLL" > "$OUT/app.out" 2>&1
code=$?
tail -n 3 "$OUT/journey.log" 2>/dev/null
echo "exit $code"
exit $code

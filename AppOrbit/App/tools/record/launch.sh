#!/usr/bin/env bash
# Starts Xvfb :99 and App Orbit (Release, light theme, clean prefs). Leaves both running.
export DISPLAY=:99
pkill -x Xvfb 2>/dev/null; pkill -f "[A]ppOrbit\.dll" 2>/dev/null; sleep 1
Xvfb :99 -screen 0 1440x900x24 -nolisten tcp >/dev/null 2>&1 &
sleep 1.5
rm -rf "$HOME/.local/share/App Orbit"
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE/../.."
APP_NO_HOTDESIGN=1 APP_ORBIT_THEME=light nohup dotnet AppOrbit/bin/Release/net10.0-desktop/AppOrbit.dll > "$HERE/app.out" 2>&1 &
for i in $(seq 1 60); do xdotool search --name "App Orbit" >/dev/null 2>&1 && break; sleep 0.5; done
sleep 5
echo "window: $(xdotool search --name 'App Orbit' | head -1)"

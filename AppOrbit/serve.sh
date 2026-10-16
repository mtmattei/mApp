#!/usr/bin/env sh
# Serve the App Orbit folder. ES modules and the JSON graph need an HTTP origin.
cd "$(dirname "$0")" && echo "http://localhost:${1:-8787}/index.html" && python3 -m http.server "${1:-8787}"

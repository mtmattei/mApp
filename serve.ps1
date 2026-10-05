# Serve the App Orbit folder and open it. ES modules and the JSON graph need an HTTP origin.
param([int]$Port = 8787)
Set-Location $PSScriptRoot
Start-Process "http://localhost:$Port/index.html"
python -m http.server $Port

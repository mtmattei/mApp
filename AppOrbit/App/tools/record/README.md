# Scenario recording

Records `media/app-orbit-scenario.mp4`: a 72-second triage scenario driven with
real X11 input (xdotool) against the Release build under Xvfb, with captions
burned in from the timings the script logs.

```sh
dotnet build AppOrbit/AppOrbit.csproj -c Release -f net10.0-desktop
tools/record/launch.sh      # Xvfb :99 + the app, clean preferences
tools/record/record.sh      # raw.mp4 + captions.tsv next to the script
```

`python3 tools/record/captions.py` turns the timings into an ASS file, burned in with
`ffmpeg -i raw.mp4 -vf ass=captions.ass ...`. Click positions assume the
1440 × 900 design viewport and the Orderly graph; re-check them after a
layout change.

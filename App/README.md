# App Orbit (Uno Platform)

The Uno Platform version of the App Orbit prototype one folder up: the same
app graph, the same layout rules, the same shell, drawn with SkiaSharp inside
a XAML page. One project, `net10.0-desktop` (Windows, and Linux under X11 for
the headless checks), Uno.Sdk 6.8.0-dev.12.

```sh
cd AppOrbit/App
dotnet build AppOrbit/AppOrbit.csproj
dotnet run --project AppOrbit/AppOrbit.csproj -f net10.0-desktop
```

`--focus=screen.checkout` (or `APP_ORBIT_FOCUS`) opens on an entity, the way
the web prototype's `#entity-id` does. `APP_ORBIT_THEME=dark|light` pins the
theme; `APP_ORBIT_SIZE=WxH` the launch size.

## What is where

```
AppOrbit/
  Graph/      the App Graph records, index and queries (graph.js)
  State/      AppState, the Store (one writer), Prefs (LocalSettings)
  Layout/     Layout.cs: (focus, lens, view, mode) → cards and links (layout.js)
  Scene/      the viewer: Camera (the CSS 3D pipeline as one matrix), SceneGeometry (shapes,
              hit regions, anchors), SceneRenderer (cards, previews, connectors), SceneCanvas
              (SKCanvasElement: pointer, wheel, camera loop), PreviewPainter, Palette, Icons, Fonts,
              FrameLoop (one 60 Hz timer for everything that animates)
  Dock/       DockController: lift, carry, pull, ghost, settle (dock.js)
  Figure/     OrbitFigure: the Hairline figure in the inspector, on Iso.cs (from the exploded app)
  Shell/      ShellPage partials: inspector, editor, search, help, figure, journey hooks
  Themes/     Tokens.xaml (Light/Dark ThemeDictionaries, type, spacing, motion), Controls.xaml
  Graph/orderly.graph.json  ← linked from ../../graph: one graph for every renderer
tools/
  dump-layouts.mjs   runs the prototype's layout.js over every case → fixtures/layouts.json
  LayoutCheck/       recomputes every case in C# and diffs (888 cases, 2004 cards, 1110 links)
  run-xvfb.sh        headless journey under Xvfb, light or dark, screenshots into shots/
```

## How parity is checked

Three gates, in order of strength:

1. **Layout fixture.** `node tools/dump-layouts.mjs` writes what the
   prototype's own `layout.js` produces for every (focus, lens, view, mode);
   `dotnet run --project tools/LayoutCheck` recomputes each case with
   `Layout/Layout.cs`. Zero differences.
2. **Journey runner.** With `APP_ORBIT_JOURNEY=1` the app drives its own store
   through the prototype's smoke test (`../tests/smoke.mjs`): orientation,
   relationship tracing, inspection, the editor sync, the inferred edge,
   fidelity, card moves, flat view, the docking path, reduced motion, the
   figure, the keyboard cursor, pointer hit tests on the projected geometry.
   68 checks, `tools/run-xvfb.sh` (and `tools/run-xvfb.sh dark`).
3. **Screenshots.** One per stop in `shots/`, compared by eye with
   `../tests/screenshots/`.

The viewer is drawn, not composed: one `SKCanvasElement` places every card
through the same perspective pipeline the CSS used (perspective 1400, scale,
pitch, yaw), so the scene lands where the prototype's does, and connectors
run between the same anchors. The shell around it is XAML.

## Decisions

- One store and render functions, as the prototype. MVUX was not used: the
  state is synchronous and frame-coupled, and the page is a single-page tool.
- SkiaSharp for the viewer. Known good in this container, exact camera
  maths, one invalidation per change; the tradeoff is that cards are drawing,
  so the inspector's relationship list is the text equivalent of the scene.
- A dispatcher timer for animation, never a held `CompositionTarget.Rendering`:
  the compositor stops raising frames when nothing is dirty on X11, and pumps
  the display forever on Win32.

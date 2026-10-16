# App Orbit

**A spatial inspector for Uno Platform apps.** Before you change a screen in code you didn't
write, App Orbit shows you everything it touches: the view model behind it, the members each
control binds to, the states it can be in, the routes in and out, and the shared components it
reuses. You zoom from the whole app down to a single property, and every fact links to the
source line it came from.

![The Checkout screen in the Behavior lens: controls on the left, CartViewModel behind them, the inspector on the right](AppOrbit/App/shots/04-checkout-behavior.png)

| | |
|---|---|
| **Main project** | App Orbit, in [`AppOrbit/`](AppOrbit/README.md) |
| **Status** | Working prototype on hand-written graphs of two sample apps. Next step: generate the graph from real source. |
| **Stack** | .NET 10, Uno Platform (Uno.Sdk 6.7.30), `net10.0-desktop`, Skia renderer, SkiaSharp viewer |
| **Legacy** | Atlas, the navigation map this project grew out of: the `Atlas.*` projects at the root. See [Atlas (legacy)](#atlas-legacy). |

---

## Contents

- [Five-minute orientation](#five-minute-orientation)
- [Run it](#run-it)
- [How App Orbit works](#how-app-orbit-works)
- [Repository map](#repository-map)
- [Code tour](#code-tour)
- [Working on it](#working-on-it)
- [How we know it works](#how-we-know-it-works)
- [Known limits](#known-limits)
- [Roadmap](#roadmap)
- [Atlas (legacy)](#atlas-legacy)
- [Docs index](#docs-index)
- [Credits](#credits)

---

## Five-minute orientation

1. **Watch it.** [`AppOrbit/App/media/app-orbit-scenario.mp4`](AppOrbit/App/media/app-orbit-scenario.mp4)
   is a 72-second captioned walkthrough: a bug ticket diagnosed without opening a file.
2. **Run it.** Two commands, [below](#run-it).
3. **Try the story yourself.** [`AppOrbit/App/DEMO.md`](AppOrbit/App/DEMO.md) is the four-minute
   demo script. Following it once is the fastest way to learn the app.
4. **Read the design.** [`AppOrbit/App/SPEC.md`](AppOrbit/App/SPEC.md) explains the architecture
   and the decisions behind it.
5. **Check what is finished.** [`AppOrbit/App/COMPLETENESS.md`](AppOrbit/App/COMPLETENESS.md)
   rates every area (screens, navigation, states, accessibility, performance, testing) with the
   evidence and the known gaps.

---

## Run it

### Prerequisites

| Tool | Needed for |
|---|---|
| .NET 10 SDK | Building and running the app |
| Uno Platform setup (`dotnet tool install -g uno.check` then `uno-check`) | First-time Uno workloads on a new machine |
| Visual Studio 2022+ or Rider (optional) | Debugging, Hot Reload, Hot Design |
| Python 3 | Graph validation and the graph generators (stdlib only) |
| Node 18+ | The browser prototype's tests and the layout parity fixture |

The app pins its own Uno.Sdk in [`AppOrbit/App/global.json`](AppOrbit/App/global.json). The
root `global.json` pins the same version for the Atlas projects.

### The desktop app

```powershell
cd AppOrbit\App
dotnet run --project AppOrbit\AppOrbit.csproj -c Release -f net10.0-desktop
```

Or open `AppOrbit\App\AppOrbit.sln` in Visual Studio and run the `AppOrbit (Desktop)` profile.

> **Use Release outside an IDE.** A Debug build calls `UseStudio()` and waits for the Hot Design
> server, so the window stays blank from a plain `dotnet run`. Set `APP_NO_HOTDESIGN=1` if you
> need a Debug build from the command line.

Launch options, as an environment variable or an argument:

| Option | Does |
|---|---|
| `APP_ORBIT_GRAPH=<path>` or `--graph=<path>` | Inspect another graph file instead of the bundled Orderly |
| `APP_ORBIT_FOCUS=<id>` or `--focus=<id>` | Open on an entity, for example `screen.checkout` |
| `APP_ORBIT_THEME=dark` or `light` | Pin the theme |
| `APP_ORBIT_SIZE=1440x900` | Launch size |

To open the second sample app:

```powershell
$env:APP_ORBIT_GRAPH = (Resolve-Path ..\graph\fieldwork.graph.json)
dotnet run --project AppOrbit\AppOrbit.csproj -c Release -f net10.0-desktop
```

### The browser prototype

```powershell
cd AppOrbit
.\serve.ps1        # serves on :8787 and opens the browser
```

### Keys worth knowing

| Key | Does |
|---|---|
| `1` `2` `3` `4` | Lens: Structure, Navigation, Behavior, States |
| `/` | Search, then `Enter` to jump |
| `Backspace` | Zoom out one level |
| `Alt` + `←` | Back along your trail |
| `D` | Dock the viewer so the inspector gets the room |
| `F` / `W` | Flat view / wireframe or UI previews |
| `0` | Reset the camera |
| `?` | Every shortcut |

---

## How App Orbit works

### The input: an app graph

Everything App Orbit shows comes from one JSON file that describes an app
([schema](AppOrbit/graph/app-graph.schema.json)):

- **Nodes:** `feature`, `screen`, `component`, `component-instance`, `viewmodel`, `property`,
  `command`, `state`, `route`.
- **Edges:** `belongs-to`, `contains`, `instance-of`, `uses-viewmodel`, `exposes`, `binds-to`,
  `invokes`, `navigates-to`, `has-state`, `transitions-to`, `depends-on`, `entered-via`.
- **Evidence on every node and edge:** a kind (`observed`, `declared`, `derived`, `inferred`),
  a confidence, and a source reference (file and line) into the graph's own source excerpts.
  Inferred edges render dashed and carry their rationale.
- **Definitions and instances are separate.** A shared component is one node; each place it
  appears is a `component-instance`. That is how "where is this used?" gets answered.

Two sample graphs ship today:

| Graph | App | Size |
|---|---|---|
| [`orderly.graph.json`](AppOrbit/graph/orderly.graph.json) | A 5-screen ordering app (bundled) | 73 nodes, 118 edges |
| [`fieldwork.graph.json`](AppOrbit/graph/fieldwork.graph.json) | An 8-screen field-service app, shaped to stress edge cases | 132 nodes, 216 edges |

### The view: levels and lenses

You move through five **levels** by zooming: Application (feature panels) → Feature (screens and
the routes between them) → Screen (the preview with its view model behind it and its states
beside it) → Component → Detail (one member, state or route with everything it reads and
everything that reads it).

At every level, a **lens** picks which relationships draw: Structure, Navigation, Behavior or
States. The front layer stays put, so the app stays recognisable as you switch.

Around the viewer: a **mock editor** that scrolls to the source of whatever you select, an
**inspector** with the facts, evidence and relationships as text, and **search**.

---

## Repository map

```
AppOrbit/                    ← the main project
  App/                         the Uno Platform desktop app (start here for code)
    AppOrbit/                    the project itself
    tools/                       parity check, headless journey, stress runner, video recording
    shots/                       one screenshot per journey stop, refreshed by each run
    media/                       the recorded walkthrough
    DEMO.md SPEC.md COMPLETENESS.md README.md
  graph/                       the app graphs and their JSON schema
  scripts/                     validate_graph.py, fieldwork_graph.py, stress_graph.py
  index.html css/ js/          the browser prototype: the reference the Uno app is checked against
  tests/                       the prototype's Playwright smoke test
  figures/                     the exploded-view and Hairline illustrations
  SPEC.md PROTOTYPE.md         the original briefs; the prototype's full guide

Atlas.Core/ Atlas.Extraction/ Atlas.Runtime/ Atlas.Agent/ Atlas.Cli/ Atlas.App/ Atlas.Tests/
Atlas.sln                    ← legacy: Atlas, the navigation map (see below)
samples/                     Atlas fixtures and RoundsApp, a reference Uno app
design/                      Atlas visual references
docs/                        Atlas specs and conventions; docs/ATLAS.md is its full guide
tools/run-atlas.ps1          builds and runs the Atlas viewer
TOOLING.md                   packaging Atlas as a tool
.github/workflows/ci.yml     CI for the Atlas projects
```

App Orbit and Atlas are separate solutions with no project references between them.

---

## Code tour

The Uno app lives in `AppOrbit/App/AppOrbit/`. The data flows one way:

```
graph JSON ──► Graph/ (records, index, queries)
                  │
user input ──► State/Store ──► AppState (one immutable record)
                                  │
                       Layout/Layout.Compute(graph, state) ──► cards and links
                                  │
                       Scene/ paints them on one SKCanvasElement
                       Shell/ updates the XAML around it (inspector, editor, search)
```

| Folder | What it holds |
|---|---|
| `Graph/` | `AppGraph` (the JSON records), `GraphIndex` (lookups), `Queries` (search, uses, dependencies) |
| `State/` | `AppState`, the `Store` (the only writer), `Prefs` (remembered settings) |
| `Layout/` | `Layout.cs`: (focus, lens, view, mode) → cards and links. A port of the prototype's `layout.js`, checked case by case |
| `Scene/` | The viewer. `Camera` (the CSS 3D pipeline as one matrix), `SceneGeometry` (shapes and hit regions), `SceneRenderer`, `SceneCanvas` (pointer, wheel, camera loop), `PreviewPainter`, `Palette`, `FrameLoop` (one 60 Hz timer) |
| `Dock/` | `DockController`: the lift, carry and settle of docking the viewer |
| `Figure/` | The exploded-view figure in the inspector |
| `Shell/` | `ShellPage` partials (inspector, editor, search, help, figure), plus the journey and stress runners |
| `Themes/` | `Tokens.xaml` (light and dark colours, type, spacing, motion) and `Controls.xaml` |

**Why it is built this way** (full reasoning in [`AppOrbit/App/SPEC.md`](AppOrbit/App/SPEC.md)):

- **One store, no MVUX.** The state is synchronous and changes with every frame of camera
  motion; a single immutable record and a pure layout function keep it predictable and testable.
- **SkiaSharp for the viewer.** It reproduces the prototype's 3D camera exactly and redraws in
  one pass. The tradeoff: cards are drawn, so the inspector's relationship list is the accessible
  text equivalent of the scene.
- **XAML for the shell.** Text, focus, search and scrolling stay native.

---

## Working on it

### Conventions

- **The browser prototype is the reference for layout and search.** A change to what cards
  appear, where, or what search returns goes into both `AppOrbit/js/layout.js` (or `graph.js`)
  and the C# port, then through the parity check.
- **The graph file is the contract.** A schema change touches four places together:
  `graph/app-graph.schema.json`, `scripts/validate_graph.py`, `App/AppOrbit/Graph/AppGraph.cs`
  and `js/graph.js`.
- **Colours, type and spacing come from `Themes/Tokens.xaml`.** No hex values in pages or code.
- **Every journey stop has a check.** New behaviour gets a check in `Shell/Journey.cs`.
- **Small steps, conventional commits** (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`,
  `build:`). Work on a branch and open a PR into `master`.
- **Claude Code users:** `docs/CLAUDE.md` describes Atlas's conventions (MVUX, Material). For work
  inside `AppOrbit/`, follow this section and `AppOrbit/App/SPEC.md` instead.

### Common tasks

| Task | Steps |
|---|---|
| Change the layout | Edit `js/layout.js` and `App/AppOrbit/Layout/Layout.cs`. Regenerate the fixture with `node App/tools/dump-layouts.mjs`, then run `dotnet run --project App/tools/LayoutCheck` until it reports 0 differences. |
| Add a sample app | Write a graph that follows the schema (or a generator like `scripts/fieldwork_graph.py`), run `python scripts/validate_graph.py <graph>`, open it with `APP_ORBIT_GRAPH`. For parity, dump a fixture for it and pass both paths to `LayoutCheck`. |
| Change the shell UI | XAML in `ShellPage.xaml` and `Themes/`, code in the matching `Shell/ShellPage.*.cs` partial. Run the journey in both themes. |
| Test at scale | From `App/`: `python ../scripts/stress_graph.py 25 8` writes a 200-screen `stress-25x8.graph.json`; build Release, then `tools/run-stress.sh stress-25x8.graph.json stress.log` |
| Re-record the walkthrough | See `App/tools/record/README.md` |

---

## How we know it works

Commands start from `AppOrbit/`.

| Check | What it proves | Command | Last result |
|---|---|---|---|
| Graph validator | Every id, edge and source line resolves | `python scripts/validate_graph.py graph/orderly.graph.json` | Both graphs pass |
| Layout and search parity | The C# layout and search match the prototype's JavaScript exactly | `cd App; dotnet run --project tools/LayoutCheck` | 0 differences: Orderly 888 cases and 175 searches, Fieldwork 1,596 and 299 |
| Journey | The app drives its own store through every surface and checks the result | `cd App; tools/run-xvfb.sh` (add `dark` for the dark theme) | 75 / 75, both themes |
| Prototype smoke test | The browser prototype still passes its own journeys | `npm run serve`, then `npm test` (setup in `PROTOTYPE.md`) | 65 checks pass |
| Stress | Speed, memory and stability at scale | `cd App; tools/run-stress.sh <graph> <log>` (Release build first) | Passes up to a generated 200-screen app (8,226 nodes) |

Every check last ran on Linux. The journey and stress scripts need Xvfb; on Windows, run the
app and walk through `App/DEMO.md` by hand. **None of these run in CI yet:** the workflow in
`.github/workflows/ci.yml` builds and tests the Atlas projects only.

---

## Known limits

- **The graphs are hand-written.** Nothing reads a real codebase yet. See the roadmap.
- **Not yet run on Windows.** Windows is the target and every automated check ran on Linux, so
  rehearse on the demo machine before showing it.
- **Mouse-wheel zoom is unverified.** Uno's X11 host reads the wheel through XInput 2, which the
  headless display does not provide. Test it by hand on Windows.
- **No live values.** The inspector says so: no running app is connected.
- **A small framework leak remains.** Uno keeps a weak reference for every element that uses a
  shared brush. The app avoids shared brushes where it builds content in code, which cut the
  growth tenfold, and managed memory levels off in the stress runs. Details in
  `AppOrbit/App/COMPLETENESS.md`.

---

## Roadmap

1. **Import from Atlas's extractor.** `atlas extract` already reads real Uno source with Roslyn.
   On `samples/RoundsApp` it finds 13 screens and 23 declared edges, 11 of them flow triggers
   labelled with their button text ("Sign in", "Start rounding"). An importer from Atlas's
   `AppModel` to an App Orbit graph gives real screens and routes immediately.
2. **Extract what Atlas does not:** view models, members, bindings, commands and states, from
   XAML and C#. This is the bulk of the work.
3. **Run and verify on Windows,** including the mouse wheel.
4. **Add App Orbit's checks to CI:** validator, parity and the journey.
5. **Live values** from a running app, building on Atlas's runtime agent.

---

## Atlas (legacy)

Atlas is the project this repository started as. It renders an Uno app's **navigation**
(screens and routes) as a canvas, and marks each edge as declared in code, observed at runtime,
or unreachable. App Orbit took its core idea, a structural map with evidence on every edge, one
level deeper into each screen.

Atlas is no longer developed; its last feature work was in June 2026. Two parts of it matter
for App Orbit's roadmap and are worth knowing:

- **`Atlas.Extraction`**: the Roslyn route and trigger extractors, run by the `atlas extract`
  CLI in `Atlas.Cli`.
- **`Atlas.Agent` and `Atlas.Runtime`**: a small package an app references to stream its
  navigation over a local socket, and the merger that marks routes as observed.

It still builds, and CI still covers it:

```powershell
dotnet build Atlas.sln
dotnet test Atlas.Tests/Atlas.Tests.csproj        # 68 tests
dotnet run -f net10.0-desktop --project .\Atlas.App\Atlas.App.csproj
dotnet run --project .\Atlas.Cli\Atlas.Cli.csproj -- extract .\samples\RoundsApp\RoundsApp\App.xaml.cs --source .\samples\RoundsApp\RoundsApp --out rounds.json
```

The full guide (architecture, projects, CLI options, the runtime agent, conventions) is in
[`docs/ATLAS.md`](docs/ATLAS.md).

---

## Docs index

| File | Read it for |
|---|---|
| [`AppOrbit/README.md`](AppOrbit/README.md) | App Orbit's folder guide |
| [`AppOrbit/App/README.md`](AppOrbit/App/README.md) | The Uno app: structure, parity gates, stress, decisions |
| [`AppOrbit/App/SPEC.md`](AppOrbit/App/SPEC.md) | Architecture, design and interaction briefs, the audit, risks |
| [`AppOrbit/App/COMPLETENESS.md`](AppOrbit/App/COMPLETENESS.md) | What is finished, with evidence and known gaps |
| [`AppOrbit/App/DEMO.md`](AppOrbit/App/DEMO.md) | The demo script |
| [`AppOrbit/PROTOTYPE.md`](AppOrbit/PROTOTYPE.md) | The browser prototype, its tests and figures |
| [`AppOrbit/SPEC.md`](AppOrbit/SPEC.md) | The original App Orbit briefs |
| [`docs/ATLAS.md`](docs/ATLAS.md) | Atlas (legacy): the full guide |
| [`docs/SPEC.md`](docs/SPEC.md), [`docs/CLAUDE.md`](docs/CLAUDE.md), [`TOOLING.md`](TOOLING.md) | Atlas (legacy): spec, conventions, tooling |

---

## Credits

The exploded-view figure follows Cathryn Lavery's
[diagram-design](https://github.com/cathrynlavery/diagram-design) skill, and the interactive
figures are built with Lucas Marques's [hairline](https://github.com/lucasmarkes/hairline).
Both are MIT and vendored unchanged; see [`AppOrbit/PROTOTYPE.md`](AppOrbit/PROTOTYPE.md#figures).

# App Orbit (Uno) — Spec

**Status:** implemented. Parity with the web prototype one folder up
(`../SPEC.md`, `../tests/smoke.mjs`) is checked by the three gates below; the
results are in README.md. The prototype's Design and Interaction
briefs are the reference for *what* the app does; this spec says *how* the Uno
app does it and only restates behaviour where the platform changes it.

**Parity definition:** every check in `../tests/smoke.mjs` has an equivalent
that passes against the Uno app (the layout parity fixture, the journey runner
and the screenshots under `shots/`), and the success test holds: someone who
does not know Orderly can say which view model controls Checkout, where
**Place order** navigates, and where `OrderLineRow` is used, without reading a
file tree.

---

## Architecture Brief

### Targets and SDK

- `net10.0-desktop` only for this version (Windows via Win32, Linux via X11
  for headless verification in this container). WebAssembly and mobile are a
  later decision; nothing here prevents them except the SKCanvasElement guard.
- `Uno.Sdk 6.8.0-dev.12`, pinned in `global.json`. Known good in this
  container with the `exploded` app (SkiaSharp 3.119, SKCanvasElement, Xvfb).
- `UnoFeatures`: `SkiaRenderer`. No Toolkit, no Material, no Navigation
  extensions, no MVUX (see the pattern decision).

### Pattern

Decision: a single store (`AppState` record + `Store.Dispatch`) and render
functions in code-behind, the prototype's architecture, with `x:Bind` only for
static shell wiring.
Reason: the state is synchronous and frame-coupled (hover, cursor, camera,
carrying). Nothing is async after the graph loads. MVUX feeds would add a
projection layer between a 60 fps canvas and its state for no gain, and the
scaffolding rules reserve MVVM-lite for single-page tools, which this is.
Tradeoff: no generated bindable surface; the shell re-renders its panels from
store subscriptions the way the prototype's modules do. Do not switch
mid-project.

### Shell and regions

One page, `ShellPage`. There are no routes: levels, lenses, view and mode are
state. Layout is a Grid:

```
Row 0  TopBar       brand · breadcrumb · search · lens tabs · toggles
Row 1  Shell Grid   [Editor 300] [ViewerSlot *] [Inspector 320]
                    + Viewer (absolute, in a Canvas over the shell) + DockGhost
```

The viewer is always positioned absolutely (prototype v0.1.3): its two homes
are the slot's rect and the dock rect (380×260, 16 px from the bottom-right).
Docked mode collapses the slot column to 0 and the inspector takes the width.

### Modules

```
AppOrbit/App/AppOrbit/
├─ Graph/        AppGraph.cs (records + JSON), GraphIndex.cs (byId/out/in/byType), Queries.cs (graph.js)
├─ State/        AppState.cs (record), Store.cs (dispatch + Changed), Prefs.cs (LocalSettings)
├─ Layout/       Layout.cs (layout.js: Card, Link, Frame sizes, per-level functions)
├─ Scene/        SceneCanvas.cs (SKCanvasElement host: camera, input), SceneRenderer.cs (cards, previews,
│                links, hit regions), Preview.cs (preview.js), Palette.cs (tokens → SKColor)
├─ Shell/        ShellPage.xaml(.cs), Breadcrumb, Search, Inspector, Editor, Help (render functions)
├─ Dock/         Dock.cs (dock.js: springs, homes, ghost, carry)
├─ Figure/       Iso.cs (Hairline geometry, from exploded), OrbitFigure.cs (the inspector's figure)
├─ Themes/       Tokens.xaml (Light/Dark ThemeDictionaries), Type.xaml (text styles)
└─ Graph/orderly.graph.json  ← linked Content from ../../graph (single source of truth)
```

### Data flow

```
orderly.graph.json ──load (ms-appx)──▶ GraphIndex (read-only after load)
                                          │
                 AppState (focus, lens, view, mode, camera is scene-local, hover, cursor, trail,
                           reducedMotion, fidelity, editor, carrying, sceneVersion)
                                          │
                       Layout.Compute(state) ──▶ LayoutResult { Cards, Links, Note, Level, Bounds }
                                          │
                  SceneCanvas renders cards + links on one SKCanvasElement, one Invalidate per change;
                  hit regions are recorded while drawing and used for hover / click / wheel / drag
                                          │
           ShellPage subscribes: breadcrumb, viewer title/note, inspector, editor, toggles, prefs
```

Single writer: `Store.Dispatch`. Hover and cursor go through the store too (as
in the prototype) but only re-render highlights, not the layout. Camera lives
in the scene (not in the store), as in `scene.js`.

### Services

None beyond the platform: `StorageFile` (graph asset),
`ApplicationData.Current.LocalSettings` (prefs, card offsets), the window's
`DispatcherQueue`. No DI container; the app is one page.

### Platform constraints and fallbacks

- `SKCanvasElement` needs Skia rendering. The guard throws with a clear
  message elsewhere (WinAppSDK native is not a target).
- Reduced motion: no reliable OS query on Skia desktop; the toggle is the
  source, persisted. (`UISettings.AnimationsEnabled` is a later spike.)
- Fonts: system UI stack. `Segoe UI` on Windows, the platform default sans on
  Linux. Sizes are fixed numbers from the prototype (11, 12.5, 13, 15, 20);
  the Skia renderer uses the same family via `SKTypeface.FromFamilyName`.
- Hot Design is not used in this app (SKCanvasElement goes blank after a
  round-trip, see the scaffolding gotchas). `UseStudio()` is gated by
  `APP_NO_HOTDESIGN=1` so headless runs show a window.
- `CompositionTarget.Rendering` is subscribed only while something animates
  (camera easing, dock flight, settle pulse) and released when it settles.

### Testing and validation

1. **Layout parity fixture.** `tools/dump-layouts.mjs` runs the prototype's
   pure `layout.js` in Node over the same graph for a list of
   (focus, lens, view, mode) cases and writes `tools/fixtures/layouts.json`
   (card keys, kinds, positions, sizes, link endpoints, notes).
   `tools/LayoutCheck` (console app referencing the app's Graph and Layout
   sources) recomputes each case in C# and diffs. Zero differences is the gate
   for the Layout and Graph modules.
2. **Journey runner.** With `APP_ORBIT_JOURNEY=<name>` the app drives its own
   store through the smoke test's steps (focus, lens, zoom out, back, search,
   fidelity, flat, dock) and asserts the same facts (`focusId`, breadcrumb,
   card counts, hot members, inspector text), printing `ok`/`FAIL` lines to a
   log file and saving a screenshot per stop under `shots/`. Runs under Xvfb
   here and on Windows.
3. **Pointer and keyboard.** Clicks, wheel, drag and keys are exercised through
   the journey runner's injected input on the canvas (it calls the same
   handlers the pointer events call) and by hand on Windows.
4. **Screenshots** compared by eye against `../tests/screenshots/`.

---

## Design Brief

Reference: the prototype's Design Brief. What changes on Uno:

- **Tokens** live in `Themes/Tokens.xaml` as `ThemeDictionaries` (Light,
  Dark) with the prototype's values: paper/paper-2/paper-3, ink/ink-2/ink-3,
  rule/rule-2, focus/focus-soft, danger, ok, the six entity hues, ui-accent.
  Every brush in XAML is a `ThemeResource`. Non-colour tokens (spacing 4-pt,
  radius 8, control height 28, dock 380×260×16, motion 420/180/120/240 ms) are
  plain resources.
- **Skia palette** is read from the resource dictionary once per theme change
  (`Palette.FromResources`) and snapshotted into the renderer. The canvas
  never resolves XAML resources on the render thread.
- **Type**: five sizes as `TextBlock` styles (`T11`, `T12`, `T13`, `T15`,
  `T20`), mono via `FontFamily` resource (`Cascadia Mono, Consolas` on
  Windows; `monospace` fallback). No `CharacterSpacing` (silent no-op on
  Skia).
- **Layout**: 1440×900 is the design viewport. Columns 300 / * / 320; under
  1400 the inspector is 280; under 1100 the editor collapses (VisualStateManager
  with `AdaptiveTrigger`).
- **Cards** are drawn: fill paper, 1 px rule stroke, radius 8, focal cards
  (screen, view model, detail) carry the soft shadow (drawn as a blurred
  rounded rect under the card, Skia `MaskFilter`). No single coloured borders;
  the hue appears in the glyph and the head rule, as in the prototype after
  the craft pass.
- **Icons**: the 12 px entity icon set, as Skia paths in the canvas and as
  `PathIcon` in XAML (the same path data, from `Icons.cs`).

---

## Interaction Brief

Reference: the prototype's Interaction Brief and input table. Uno mapping:

| Prototype input | Uno |
|---|---|
| click card / region / member | `PointerReleased` on the canvas without movement → hit region → `Focus(id)` |
| hover | `PointerMoved` → hit region → `hoverId` (store) |
| wheel semantic zoom | `PointerWheelChanged` on the viewer; same thresholds (1.6 in, 0.62 out), same 450 ms momentum lock renewed at 250 ms |
| drag empty scene | `PointerPressed` + `CapturePointer` after 5 px; yaw += dx·0.25, pitch −= dy·0.18, clamped ±40/±22 |
| drag card | same, offsets in scene units undoing scale and foreshortening; persisted per layout id |
| keys | `PreviewKeyDown` on the page: `/ ? + − Backspace Esc 0 1–4 F D W`, arrows (orbit), Shift+arrows (nudge), Alt+← (back), Tab/Shift+Tab (cursor through cards when the viewer has focus), Enter (focus cursor) |
| viewer head drag | `Dock.Lift/Carry/Release` with the prototype's springs (k190 c26, k240 c30), reach 210, pull 55 %, settle 1.2 % over 180 ms |
| search | `TextBox` + results `ListView`; ↑ ↓ Enter Esc |
| help | `ContentDialog` (keyed style, not implicit) with the shortcut list and workspace root |

States: loading (graph read, under 100 ms, no skeleton needed on desktop; the
window opens on the application level), load error (one panel with the asset
path), empty lens notes (from `Layout.Note`), runtime always "unavailable".

Motion: camera easing 0.18 per frame toward target (as `scene.js`), focus
crossfade 180 ms (canvas alpha ramp), connector draw 240 ms (dash offset),
dock springs. Reduced motion: every change resolves in one step; ghosts stay.

Accessibility: the canvas is one focusable element with
`AutomationProperties.Name="Orbital viewer"`; the inspector's relationship
list is the text equivalent of the connectors (as in the prototype); all
shell controls are real buttons with names; contrast as the prototype.

Runtime verification: journey runner under Xvfb (`tools/run-journeys.sh`),
screenshots in `shots/`, then the same on Windows by hand.

---

## Spec Graph Brief

### Route tree

```
ShellPage                         no navigation extensions; one page
└─ level × lens × view × mode     state in Store, not routes
```

### Page tree

```
ShellPage
├─ TopBar
│  ├─ Brand
│  ├─ Breadcrumb (ItemsControl of Buttons)      ← ContextChain(focus, trail)   → Focus(id)
│  ├─ Search (TextBox + ListView)               ← Queries.Search               → Focus(id)
│  ├─ LensTabs (4 ToggleButtons)                ← state.Lens                   ⇒ SetLens
│  └─ Toggles: Wire/UI · Flat · Motion · Dock · ?   ⇒ fidelity, view, reducedMotion, Dock.Toggle, Help
├─ Shell Grid
│  ├─ Editor: FileList (ItemsControl) + CodePane (ItemsRepeater of lines)   ← graph.files, state.editor ⇒ Focus(id, line)
│  ├─ ViewerSlot (empty Border; measured for the expanded home)
│  ├─ Inspector (StackPanel, re-rendered)        ← node, in/out edges, evidence  → Focus, Hover, OpenSource, SetLens
│  │  └─ OrbitFigure (SKCanvasElement)           → SetLens
│  ├─ DockGhost (Border, hairline)               ← Dock preview home
│  └─ Viewer (Grid, absolutely placed)
│     ├─ ViewerHead: grip · back · title · note · expand/dock   ⇒ Dock.Lift (drag), Back, Dock.Toggle
│     ├─ SceneCanvas (SKCanvasElement)           ← Layout(state), camera, hover/focus/cursor; ⇒ hover, focus, zoom, offsets
│     └─ ViewerFoot: camera readout · reset view · reset layout · hint
└─ Help (ContentDialog)                         ← shortcuts; workspace root ⇒ state.WorkspaceRoot
```

### Data-flow graph

```
orderly.graph.json → GraphIndex (writer: load only)
Store.AppState
  focusId   ⇐ card hit, region hit, member hit, zoom in/out, search, editor line, breadcrumb, inspector rel, route chip, journey
  hoverId   ⇐ canvas pointer, inspector rel hover
  cursorId  ⇐ Tab / Shift+Tab on the viewer
  lens      ⇐ tabs, keys 1–4, figure pick, inspector "find uses"
  mode      ⇐ Dock (lift preview and settle), D, buttons
  view      ⇐ F, toggle
  fidelity  ⇐ W, toggle
  reducedMotion ⇐ toggle
  editor    ⇐ focus (node.source), file click, source links
  carrying  ⇐ Dock.Lift / Settle
Scene camera (local) ⇐ drag, arrows, wheel, reset, view change
Offsets (LocalSettings) ⇐ card drag, Shift+arrows, reset layout, double-click
Prefs (LocalSettings)   ⇐ lens, mode, view, reducedMotion, fidelity, workspaceRoot
```

### Design graph

```
Themes/Tokens.xaml @App  (ThemeDictionaries Light / Dark)
├─ Paper, Paper2, Paper3, Ink, Ink2, Ink3, Rule, Rule2     ThemeResource   surfaces and type
├─ Focus, FocusSoft                                         ThemeResource   cue: 2 px ring + raised plane
├─ Danger, Ok                                               ThemeResource   cue: text label beside colour
├─ Hue.Screen|Feature|Vm|State|Route|Comp                   ThemeResource   cue: glyph shape + head rule
├─ UiAccent, UiAccentSoft                                   ThemeResource   previews only
├─ Motion.Orbit 420 ms · Motion.Fade 180 ms · Motion.Hover 120 ms · Motion.Draw 240 ms   reduced: instant
├─ Space.1..6 (4,8,12,16,24,32) · Radius 8 · ControlHeight 28 · Dock.W 380 · Dock.H 260 · Dock.Gap 16
└─ Font.Ui (system) · Font.Mono · T11 T12 T13 T15 T20 styles
Scene/Palette.cs  ← the same keys, snapshotted to SKColor per theme
```

### Spec gate

| Check | Result | Nodes | Resolution |
|-------|--------|-------|------------|
| Registration / Region host / Shell safety / Cross-page / Back stack | n/a | one page, no routes; back is the trail | |
| Sources | PASS | every node reads the store or the graph index | |
| Feed vs state | n/a | no MVUX; the store is the one mutable thing | |
| Single writer | PASS | Store.Dispatch; camera and offsets are scene-owned | |
| FeedView states | n/a | one synchronous load; load error panel declared | |
| Failure paths | PASS | load error panel; validator for the graph (`../scripts/validate_graph.py`) | |
| Data shape | PASS | same graph as the prototype | |
| Tokens | PASS | every brush and size in XAML resolves to Tokens.xaml; Skia reads the same keys | |
| Theme-awareness | PASS | colours in ThemeDictionaries; sizes static | verify dark on Windows |
| Scope | PASS | single dictionary merged in App.xaml | |
| Color cue | PASS | glyph + head rule per role; dashed stroke + "inferred" tag | |
| Contrast | PASS | prototype values | verify in shots |
| Encodings | PASS | three fixed card frames per level | |
| Motion | PASS | four motion tokens with reduced fallbacks; springs listed | |
| Type assets | PASS | system fonts only | |
| UnoFeatures | PASS | SkiaRenderer covers SKCanvasElement | |
| Targets | PASS | desktop only; guard message elsewhere | |
| Pending | PASS | ? pending: OS reduced-motion query; dark theme on Linux | |

---

## Implementation Plan

1. ✔ Scaffold (this spec, project from the exploded skeleton, tokens), build green.
2. ✔ Graph records + index + queries; Layout port; `tools/dump-layouts.mjs` + `tools/LayoutCheck`: 888 cases, zero diffs.
3. ✔ Scene renderer: camera, cards of every kind, previews (wire and UI), links with labels and arrows, hit regions, hover/focus/cursor rings.
4. ✔ Shell: top bar, breadcrumb, search, lens tabs, toggles, inspector, editor, help; journeys 1–3 pass in the runner.
5. ✔ Input: wheel zoom, orbit drag, card drag + offsets, keyboard map, flat view, reduced motion, fidelity.
6. ✔ Docking: lift, carry, pull, ghost, settle, D; reduced motion in one step.
7. ✔ Orbit figure in the inspector (Iso.cs from exploded).
8. ✔ Parity pass: 70 journey checks in both themes, screenshots per stop, README for the app.

## What was learned building it

- A page's `Resources` indexer does not see App.xaml or the theme entries; code-behind resolves
  tokens through the application dictionary and the active theme (Shell/Res.cs).
- Lightweight-styling keys for the stock templates only take at the application level, and a
  checked `ToggleButton` keeps its accent fill either way; the lens tabs and toggles are buttons
  whose pressed look the page sets.
- `CompositionTarget.Rendering` starved the springs on X11 (four frames in 450 ms once nothing
  else was dirty); one dispatcher timer at 16 ms drives every animation and stops when idle.
- `AppWindow.Resize` after creation races the X11 surface; `ApplicationView.PreferredLaunchViewSize`
  before the window exists is deterministic, with a re-ask loop as the fallback.
- `SKCanvasElement` is not focusable; a `ContentControl` host with `IsTabStop` carries the keyboard.

## Risks

- Resolved: keyboard focus (ContentControl host), wheel delta (+120 per notch up), LocalSettings on Linux (works; guarded), theme resources (read by theme name), the frame loop (dispatcher timer).
- Open: text on Linux uses Liberation Sans, narrower than DejaVu but wider than Segoe UI; a short head can still truncate on the `sm` frame. Layout numbers never depend on measured text.
- Open: the Win32 host has not run in this container; the first Windows run should check the window size, the wheel direction and the dock springs by hand.

## Unresolved Questions

- WebAssembly target: the prototype runs in a browser already; an Uno WASM build would be a second web renderer. Deferred.
- OS reduced-motion: toggle only until a reliable query exists on Skia desktop.
- The in-editor "open in VS Code" link: `vscode://` launch via `Launcher.LaunchUriAsync` on desktop is untested here (no VS Code in the container).

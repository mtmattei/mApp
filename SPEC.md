# App Orbit — Spec (v0.1 prototype)

**Status:** implemented (read-only prototype). `tests/smoke.mjs` passes the three
journeys; screenshots in `tests/screenshots/`.
**Scope:** a spatial app inspector. Semantic zoom over an app graph, an orbital
layered viewer for the focused screen or component, a docked and an expanded
mode, four relationship lenses, a flat view, keyboard navigation and reduced
motion. One hand-authored graph for one sample app ("Orderly").

Out of scope for v0.1: editing, agent-driven changes, repository extraction,
live runtime connection, execution traces, automatic state discovery.

The success test: someone unfamiliar with Orderly can say which view model
controls Checkout, where **Place order** navigates, and where `OrderLineRow`
is used, without reading a file tree.

---

## Architecture Brief

### Module structure

```
AppOrbit/
├─ index.html                 shell: mock editor + viewer + inspector
├─ css/app-orbit.css          tokens, layout, card and lens styles
├─ js/
│  ├─ main.js                 boot, store wiring, keyboard map
│  ├─ store.js                single state object + subscribe/dispatch
│  ├─ graph.js                load/validate/index the app graph; queries
│  ├─ layout.js               (focus, lens, view, mode) → placed cards + links
│  ├─ scene.js                DOM scene: layers, cards, previews, SVG connectors, camera
│  ├─ preview.js              wireframe screen previews from node.preview specs
│  ├─ inspector.js            details, evidence, relationships, runtime panel
│  └─ editor.js               mock editor: file list + code pane ↔ graph sync
├─ graph/
│  ├─ app-graph.schema.json   JSON Schema for the app graph (v0.1)
│  └─ orderly.graph.json      hand-authored sample graph
├─ scripts/validate_graph.py  stdlib-only integrity checks
├─ tests/smoke.mjs            Playwright journey test (uses global playwright)
└─ serve.ps1 / serve.sh       static server + open browser
```

No build step. No runtime dependencies. ES modules require an HTTP origin, so
the folder is served with `python -m http.server` (or `npx serve`).

### State model

One store, one writer (`dispatch`). Everything else derives.

```
state
├─ graph            loaded + indexed graph (read-only after load)
├─ focusId          null (application level) | entity id
├─ lens             'structure' | 'navigation' | 'behavior' | 'states'
├─ mode             'docked' | 'expanded'
├─ view             'orbit' | 'flat'
├─ camera           { yaw, pitch, scale }      clamped; scale drives semantic zoom
├─ hoverId          entity under pointer / keyboard cursor
├─ trail            [ids] visited, for breadcrumb context and zoom-out
├─ reducedMotion    media query OR user toggle
├─ runtime          { connected: false }       v0.1 constant
└─ editor           { fileId, line }           mock editor cursor
```

Derived:
- `level` = f(focus type): none→application, feature→feature, screen→screen,
  component-def / component-instance→component, member / state / route→detail.
- `contextChain(focusId, trail)` = breadcrumb. Uses `belongs-to` for screens,
  `contains` for instances, `exposes` for members. For entities with several
  parents (a shared view model, a shared component definition) it prefers the
  parent present in `trail`, else the first declared parent.

### Navigation model (inside the inspector)

- Focus change = dispatch `{focus: id}`. Trail appends.
- Zoom in = focus the hovered/keyboard-cursor child.
- Zoom out = focus the parent from `contextChain` (structure, not history).
- Back = previous trail entry (history). Following a route to another screen
  and coming back is a back, not a zoom out.
- Camera and lens are preserved across focus changes. Mode changes never move
  the focus or the camera.
- Mock editor selection → focus; focus → mock editor cursor (both directions,
  guarded against loops by comparing ids).

### Services / dependencies

- `graph.js` fetches `graph/orderly.graph.json`. On failure (file://) the
  shell shows a single instruction panel with the serve command.
- Browser APIs only: `matchMedia('(prefers-reduced-motion)')`,
  `requestAnimationFrame`, `getBoundingClientRect` for connector anchors,
  `localStorage` (try/catch) for lens/mode/view preferences.

### Data flow

```
orderly.graph.json ──load──▶ graph index (byId, outEdges, inEdges, byType)
                                   │
            focus + lens + view + mode + camera (store)
                                   │
                              layout.js ──▶ { layers[], cards[], links[] }
                                   │
                 scene.js renders cards; rAF loop applies camera and
                 reads anchor rects → SVG connectors
                                   │
        inspector.js / editor.js / breadcrumb / search render from the same store
```

### Platform constraints

- Chromium, Edge, Firefox, Safari current. CSS `perspective` +
  `transform-style: preserve-3d` + ES2020 modules.
- Max orbit yaw ±40°, pitch ±22°. Text on tilted planes stays legible at
  these angles at 13px or larger.
- Viewport ≥ 1100px wide for the full editor + viewer + inspector shell. Below
  that the mock editor collapses and the viewer takes the width.

### Testing / validation

- `scripts/validate_graph.py`: unique ids, edge endpoints exist, relation
  allowed for endpoint types, every node has evidence, every instance has a
  definition, every screen has a preview, every binding names a member.
- `tests/smoke.mjs`: drives the three journeys (orientation, tracing,
  inspection) headless and asserts breadcrumb, inspector and connector counts.
  Also runs the flat view and the reduced-motion path. Saves screenshots.
- Manual: the success test above, timed, with one person unfamiliar with Orderly.

### Decision notes

Decision: web prototype first, Uno/WinUI renderer later.
Reason: validate interaction before extraction; runnable and testable here.
Tradeoff: renderer is throwaway; graph + layout rules carry over.

Decision: DOM cards on CSS 3D planes, connectors in a screen-space SVG using
anchor element rects.
Reason: real cards (text, previews, buttons, focus rings) stay accessible and
selectable; no custom matrix math; works identically in flat view.
Tradeoff: connector positions are read back from layout each frame during
camera motion (cheap at < 60 cards).

Decision: graph is one JSON file, fetched. No embedded copy.
Reason: one source of truth, validated by script, reusable by other renderers.
Tradeoff: needs a local static server.

---

## Design Brief

### Visual direction

A precise drafting instrument. Paper-toned surfaces, hairline rules, calm
type, muted entity hues. Depth is restrained and always explanatory: planes
separate only to show which layer a relationship lives on. Actual wireframe
screen previews stand in for node icons.

### Layout structure (expanded mode)

```
┌──────────────────────────────────────────────────────────────────────┐
│ top bar: App Orbit · breadcrumb · search · lens tabs · view · mode   │
├────────────┬──────────────────────────────────────┬──────────────────┤
│ mock editor│ viewer (scene + SVG connectors)       │ inspector        │
│ files      │                                       │ details          │
│ code pane  │   [states]  [UI front] ← [behavior]   │ evidence         │
│            │        ↙ routes in   routes out ↘     │ relationships    │
│            │                                       │ runtime          │
└────────────┴──────────────────────────────────────┴──────────────────┘
```

Docked mode: the viewer shrinks to a 380×260 panel pinned bottom-right over
the editor; the editor takes the width; lens tabs hide; only the focus card
and its immediate navigation connections render, without labels. The
inspector stays, so selecting a line in the editor still shows the entity's
details.

### Spatial layers (screen level)

| Layer | z | Content |
|------|---|---------|
| Front | 0 | screen preview with selectable component instances |
| Behind | −240 | view model card, members aligned to the UI rows they bind |
| Beside (right) | 0, x +300, slight fan | state variants as small previews |
| Around | 0, x ∓ 420 | incoming (left) and outgoing (right) route chips |
| Definitions | +120 (closer), y below | shared component definition chips, "N uses" |

Lens decides which layers render; the front layer always renders.

### Typography

System UI stack (`Segoe UI` on Windows). Sizes: 11 (labels, mono refs),
12.5 (body), 13 (card titles), 15 (focus title), 20 (inspector heading).
Mono: `ui-monospace, Cascadia Mono, Consolas`. Tabular numerals in code pane.

### Spacing

4-pt grid: 4, 8, 12, 16, 24, 32. Cards: 12 padding, 8 radius. Panel gutters
16. Connector label padding 2/6.

### Component hierarchy

shell › (topbar, editor, viewer, inspector) › viewer › world › layer › card ›
(preview | member-row | state-thumb | route-chip | def-chip) + anchors.

### Theme usage

Tokens on `:root`, dark set under `prefers-color-scheme: dark` and
`[data-theme]`. Entity hues (saturation held low):

| Role | Light | Second cue |
|------|-------|-----------|
| screen | ink #1d2430 | heavy top rule |
| feature | slate #5b6472 | dashed outline |
| view model / member | indigo #4b55b8 | ◆ glyph |
| state | amber #a66a00 | ◐ glyph, italic name |
| route | teal #0f7b74 | arrowhead |
| component def / instance | plum #7a4e8c | ▭ glyph; instance has thin outline, def has solid |
| inferred evidence | same hue, dashed stroke, "inferred" tag | dashed + text |
| focus | blue #2457d6 | 2px ring + raised plane |

### Responsive / adaptive

≥1400: all three columns. 1100–1400: inspector narrows to 280. <1100: editor
hidden behind a toggle; viewer + inspector. Docked panel never below 320×200.

---

## Interaction Brief

### User flows

1. **Orientation.** Open → application level shows three features with their
   screens as thumbnails and the entry point marked. Click Purchase → feature
   level: Cart and Checkout previews with the route between them, the shared
   `CartViewModel` behind both. Click Checkout → screen level. Zoom out twice
   with `−` returns through Purchase to the application.
2. **Relationship tracing.** At Checkout, lens Navigation: incoming route
   from Cart, outgoing route to Orders from Place order. Lens Behavior:
   `IsEnabled` and `Command` connectors from Place order to
   `CanPlaceOrder` / `PlaceOrderCommand` on the view model plane. Lens
   Structure: `OrderLineRow` definition chip reads "3 uses"; selecting it
   lists Cart, Checkout, Orders.
3. **Inspection.** Select Place order → select `CanPlaceOrder` → inspector
   shows declared type and expression, evidence excerpts with source refs,
   runtime "unavailable" line, and **Open source** moves the mock editor to
   `CartModel.cs:20`.

### Input behaviour

| Input | Effect |
|------|--------|
| click card | focus |
| click preview region | focus component instance |
| wheel over card (scale ≥ 1.6) | semantic zoom in to that card |
| wheel out (scale ≤ 0.62) | zoom out to parent |
| drag on empty scene | orbit (clamped) |
| `+` / `Enter` | zoom in to hovered/cursor card |
| `−` / `Backspace` | zoom out to the parent context |
| `Alt+←` / back button | back along the trail (after following a relationship) |
| `Tab` / `Shift+Tab` | move keyboard cursor between cards |
| arrows (scene focused) | orbit 6° steps |
| `0` | reset camera |
| `1`–`4` | lens Structure / Navigation / Behavior / States |
| `F` | flat ↔ orbit |
| `D` | docked ↔ expanded |
| `/` | search |
| `?` | shortcut sheet |

### Empty / loading / error states

- Loading: skeleton of the three columns, no text flash.
- Graph fetch failed: one panel with the serve command (file:// case).
- No results in search: "No entity matches" line, Esc clears.
- Lens with nothing to show (e.g. States on a stateless component): the front
  layer stays, a quiet note "No declared states for this entity".
- Runtime: always "Live values unavailable — no running app connected".

### Animations / transitions

| Motion | Duration / easing | Reduced motion |
|------|------|------|
| camera orbit / explode | 420ms cubic-bezier(.2,.7,.2,1) | instant |
| focus change crossfade | 180ms ease-out | instant swap |
| card hover raise | 120ms | none |
| connector draw | 240ms stroke-dashoffset | drawn immediately |

### Feedback states

Hover: 1px ring, connectors to that card brighten. Focus: 2px blue ring,
breadcrumb updates, inspector updates, editor cursor moves. Keyboard cursor:
dotted ring distinct from focus. Evidence kind tags: declared (solid),
observed (solid, green), inferred (dashed, italic).

### Accessibility

- All cards are `<button>` with `aria-label` naming type and entity.
- Flat view is a full alternative, not a fallback.
- `prefers-reduced-motion` honoured and user-toggleable.
- Connectors are decorative to AT; the inspector's relationship list carries
  the same information as text.
- Contrast: all type ≥ 4.5:1 on both themes; entity hue never the only cue.

### Runtime verification

1. Serve and open; application level renders 3 features, 5 screens.
2. Journey 1–3 above pass in `tests/smoke.mjs`.
3. Toggle `F`: same cards, same connectors, zero rotation.
4. Enable reduced motion in the OS or the toggle: focus changes are instant.
5. Tab through the scene with the mouse unplugged; complete journey 2.

---

## Spec Graph Brief

### Route tree (inspector view states)

```
Shell
├─ level: application         ← graph.features, entry route
├─ level: feature/{id}        ← screens where belongs-to feature
├─ level: screen/{id}         ← preview, VM (uses-viewmodel), states, routes
├─ level: component/{id}      ← instance | definition, bindings, invokes
└─ level: detail/{id}         ← member | state | route
   × lens {structure, navigation, behavior, states}
   × view {orbit, flat}  × mode {docked, expanded}
```

### Page tree (viewer at screen level)

```
Viewer [scene]
├─ Breadcrumb              ← contextChain(focus, trail)
├─ FrontLayer              ← screen.preview            {n/a: graph is static}
│  └─ InstanceRegion*      → focus(instance)
├─ BehaviorLayer (lens=behavior)  ← uses-viewmodel → exposes
│  └─ MemberRow*           → focus(member)   link ← binds-to / invokes
├─ StatesLayer (lens=states)      ← has-state
│  └─ StateThumb*          → focus(state)    link ← transitions-to
├─ RoutesLayer (lens=navigation)  ← navigates-to (in/out)
│  └─ RouteChip*           → focus(screen)
└─ DefinitionsLayer (lens=structure) ← instance-of (reverse)
   └─ DefChip "N uses"     → focus(definition)
Inspector                  ← byId(focus), inEdges, outEdges, evidence
Editor                     ← files[], node.source ; ⇒ focus on line click
```

### Data-flow graph

```
orderly.graph.json → graph.js index → store.graph (writer: load only)
store.focusId   ⇐ card click, preview click, zoom, search, editor line, breadcrumb (single dispatch)
store.camera    ⇐ drag, arrows, wheel, reset (single dispatch)
layout.js       ← store → cards/links (pure)
scene.js        ← layout, camera ⇒ DOM, SVG
inspector.js    ← store
editor.js       ← store.editor ⇒ store.focusId (guarded)
```

### Design graph

```
Tokens @App
├─ --ink / --paper / --rule         cue: n/a (surfaces)
├─ --hue-screen|feature|vm|state|route|component   cue: glyph + outline style per role
├─ --focus #2457d6                  cue: 2px ring + plane raise
├─ --inferred                       cue: dashed stroke + "inferred" tag
├─ Motion.Orbit  420ms / (.2,.7,.2,1)   reduced: instant
├─ Motion.Fade   180ms / ease-out       reduced: instant
└─ Motion.Draw   240ms / linear         reduced: none
Type: system UI 11/12.5/13/15/20; mono for refs
```

### Spec gate

| Check | Result | Nodes | Resolution |
|-------|--------|-------|------------|
| Sources | PASS | all viewer nodes read from graph index | |
| Single writer | PASS | focusId has many triggers, one dispatch | |
| Data states | PASS | load error + empty lens + no runtime declared | |
| Failure paths | PASS | fetch failure panel; validator for graph | |
| Routes | PASS | levels × lens × view × mode enumerated | |
| Data shape | PASS | VM shared by two screens; def with 3 instances | |
| Scope | PASS | single token scope | |
| Pending | PASS | ? pending: real editor deep link (see Unresolved) | |
| Tokens | PASS | every colour and motion in CSS is a token | |
| Color cue | PASS | glyph/outline per role, dashed for inferred | |
| Contrast | PASS | hues chosen ≥ 4.5:1 on paper and on dark | verify in smoke screenshots |
| Encodings | PASS | card size ∝ level only (3 fixed sizes) | |
| Motion | PASS | three motion tokens with reduced fallbacks | |
| Type assets | PASS | system fonts only | |

---

## Implementation Plan

1. Graph schema + Orderly graph + validator. Run validator.
2. Shell, tokens, store, graph loader, breadcrumb, lens tabs, search.
3. Layout rules per level and lens; scene with CSS 3D layers and SVG links;
   camera with clamps, semantic zoom thresholds, reduced motion, flat view.
4. Previews (wireframe primitives), state variants, instance regions.
5. Inspector: details, evidence, relationships, "find all uses", runtime line,
   Open source.
6. Mock editor: files, code pane, line ↔ entity sync.
7. Docked mode. Keyboard map and shortcut sheet.
8. Playwright smoke test with screenshots. README.

## Unresolved Questions

- Real editor deep link: the mock editor is the default. An optional workspace
  root (in the `?` sheet, stored per browser) adds a `vscode://file` link. An
  Uno Studio protocol link is still open.
- Should zoom-out from a shared view model go to the screen you came from
  (trail) or always to a "Behavior" overview? v0.1 uses the trail.
- Layer offsets were checked at 1440×900 and 1280×720. Below 1100px the editor
  hides; below 760px the inspector stacks under the viewer.
- Whether the Uno Studio version renders with Composition, SkiaSharp, or
  PlaneProjection is a later decision and does not affect the graph.

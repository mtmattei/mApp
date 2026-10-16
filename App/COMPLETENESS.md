# Completeness check

App Orbit's stated scope is a read-only inspector over one bundled app graph,
on desktop. This is the completeness checklist applied to that scope, with the
evidence for each row. "Out of scope" means the scope excludes it, not that
it was skipped.

Evidence sources: the journey runner (74 checks, light and dark), the layout
fixture (888 cases), real-input probes under Xvfb (xdotool key presses, clicks,
window sizes, restarts, broken graph files) and screenshots of each probe.

| Area | Status | Evidence | Gaps |
|---|---|---|---|
| Screens | Complete | Every surface renders in the journey: shell, viewer expanded, lifted and docked, inspector at application, feature, screen, component, member and definition level, editor, search results, shortcut sheet with its workspace setting, boot error screen | None known |
| Navigation | Complete | Zoom in and out, breadcrumb, back along the trail, routes, search, deep link, Esc, Backspace; 20+ journey checks | A deep link to an unknown id opens the application level without saying so |
| Interactions | Fixed in this pass | Every toolbar, viewer and inspector control is driven by the journey | **Fixed:** with real key presses, `/` typed a slash into the search box, so every search failed, and `?` opened search instead of the shortcut sheet. Focus now moves on key-up, and `?` is checked first. **Unverified:** "Open in VS Code" links, which need a workspace root and an installed handler |
| Logic | Complete for the data it has | Queries, lenses and layout are computed from the graph; the layout matches the prototype's own code in 888 of 888 cases | The graph is written by hand. Nothing extracts it from source or from a running app, and the inferred relationship is authored data, not the output of an inference engine |
| Data | Complete (read-only) | Preferences and moved cards persist across a restart (probe: Behavior lens, UI previews and docking all came back) | The graph is read once at launch: a changed file needs a restart |
| States | Complete | Missing or malformed graph, empty search, no source location, live value unavailable, unresolved items, inferred versus declared, hover, pressed, focus, disabled toggles, reduced motion | No loading indicator: the scene draws about 2.3 s after a cold launch, most of it .NET startup. Long content is trimmed with ellipses; a much larger graph has not been tried |
| Forms | Complete | Search: arrows, Enter, Esc, clear button, no-match message | The workspace-root field accepts any text without checking the path exists |
| Authentication | Out of scope | Local, read-only, no accounts, no network | — |
| Responsive | Fixed in this pass | Probed at 1180×640, 1280×720, 1440×900, 1920×1080 | **Fixed:** at the old 1100 minimum the toolbar overflowed (Dock and ? off-screen); the breadcrumb clipped at 1280; breakpoints only applied after a resize, so a window opened at its final size never got them. Now: minimum 1180, the search field narrows under 1300, the breadcrumb folds its middle into "…" when it would overflow, breakpoints apply at launch. Layouts under 1180 are out of scope on desktop |
| Platform | Partly verified | Keyboard model complete; preferences survive a killed process | **Fixed, unverified on Windows:** the launch-size retry compared logical and physical pixels and kept resizing on scaled displays; it now runs only on Linux, the minimum size follows the display scale, and Windows opens maximized. Nothing has run on Windows yet |
| Failure handling | Complete | Missing and malformed graph files show a readable error screen; a reference to a missing entity no longer crashes or vanishes | **Fixed:** a missing relationship target was counted but not listed; it is now listed in red as "not in the graph" |
| Accessibility | Fixed in this pass, one gap | Every control has an automation name; full keyboard path; visible focus ring; reduced motion | **Fixed:** light-theme muted text was 3.3–3.6:1; now 4.6–5.4:1 on every light background (dark theme already passed at 4.7–5.1:1). **Gap:** a screen reader hears "Orbital viewer" but not which card has keyboard focus; the inspector is the text equivalent. Text scaling and touch-sized targets are untested |
| Performance | Complete | Cold launch to drawn scene about 2.3 s; paint on the busiest layout 0.5–0.8 ms average under software rendering; dock and camera at 60 Hz; stress-tested up to 200 screens (see below) | Not measured on Windows |
| Security and privacy | Complete | No network calls, no secrets, no telemetry; reads one bundled file, writes only local settings | — |
| Visual quality | Complete | No placeholder text or debug UI in Release; journey hooks run only under an environment variable | **Fixed:** width-capped paragraphs in the docked inspector were centred; class names broke mid-word. Wireframe previews are schematic by design |
| Testing | Partly complete | Journey ×2 themes, layout and search parity on two apps, the prototype's own smoke test, real-input scenarios on a second app, stress on four graphs, a recorded scenario | The target platform, Windows, has not been exercised |

## Verdict

Complete for its stated scope on Linux, with one accessibility gap (canvas
focus not announced) and one platform gap (no Windows run yet). The larger
product gap is outside the checklist: the graph is authored by hand, so the
app demonstrates the interaction, not extraction.

## Second pass: another app, scenarios and stress

**Another app.** `../graph/fieldwork.graph.json` (a field-service app, 132 nodes, 216 edges)
passes the project validator and the JSON schema, and the app's layout and search match the
prototype's own code on it: 1,596 cases, 3,666 cards, 2,034 links, 299 searches. Twenty-two
scenario steps were driven with real key presses in both themes: orientation, a six-state
screen, the Job ↔ Map cycle, a view model shared across features, a fifteen-member view model,
long names, a component used twice on one page, a component used nowhere, screens without
states, UI previews, flat view, docking, back navigation and keyboard focus.

**Stress.** `tools/run-stress.sh` on four graphs, Release build, software rendering under Xvfb:

| Graph | Nodes | Parse and index | Layout p99 | Step (focus and lens) | Paint per step | Result |
|---|---|---|---|---|---|---|
| Orderly | 73 | 4 ms | 6 ms | 39–50 ms average | 0.4 ms average | passed |
| Fieldwork | 132 | 7 ms | 3 ms | 49 ms average | 0.5 ms average | passed |
| Generated, 48 screens | 1,982 | 50 ms | 0.4 ms | 49 ms average | 0.6 ms average | passed |
| Generated, 200 screens | 8,226 | 290 ms | 0.9 ms | 62 ms average | 0.7 ms average | passed |

Each run: every entity in every lens, three random walks of 1,500 actions over every shell
action, 600 actions with no delay, then a check that the app still answers. The largest view
(the 200-screen application in the behavior lens, 225 cards) lays out in 20 ms warm. A
thousand random real key presses, clicks and drags on each sample app left it running with no
exceptions, and an ordinary search afterwards landed where it should.

**Found and fixed in this pass:**

- A press on an instance nested in another (a row inside a list) selected the container: hit
  regions were tested in the opposite order to the prototype's stacking. Now the topmost
  wins; a journey check guards it.
- Search sorted names by character code where the prototype uses locale order, so some
  queries returned a different set of results; and a screen and a property with the same name
  ("Job") opened the property. Search now follows the prototype's order and prefers the larger
  entity on a tie, in both versions; search parity is part of the layout gate.
- "0 uses ·", "1 uses", and "N uses across N screens" counting screens twice: uses now count
  instances ("4 uses across 3 screens"), in both versions.
- The application summary said everything else was "declared in source"; it now counts
  observed facts and says "declared in source or derived from it".
- State card headers truncated the state's name and kept the word "State"; the name now keeps
  its width and the type label gives way, as in the prototype.
- Long identifiers broke mid-word in the inspector title and evidence, clipped in relationship
  rows, and overflowed the breadcrumb; they now wrap at camelCase humps and dots, and the last
  crumb ends in an ellipsis.
- Every lens change rebuilt the inspector, including a new figure with its own drawing surface,
  and the breadcrumb: both now rebuild only when what they show changes (inspector 8 → 1.7 ms,
  breadcrumb 6 → 1.1 ms, a focus-and-lens step 97 → 44 ms), and a theme change rebuilds them in
  the new theme.
- A native path was allocated and never freed on every frame that drew a region's name chip.
- **Memory.** Heap snapshots between walks found one growing structure: Uno keeps a weak
  reference for every element that uses a shared brush and never drops it, so content rebuilt
  in code grew those lists without bound (+141,000 entries over 6,000 actions). Buttons now use
  a solid border instead of Fluent's shared gradient, and content built in code uses its own
  brushes: growth is down to +14,000 entries over the same 6,000 actions (about a quarter of a
  kilobyte per action), and managed memory levels off. The remainder comes from long-lived
  controls and is an Uno framework issue.

**Not verified here:** the mouse wheel. Uno's X11 host reads the wheel through XInput 2
scroll valuators, which the test tool's virtual pointer does not have, so wheel input never
arrives under Xvfb (a stock ScrollViewer ignores it too). The cue card uses clicks.


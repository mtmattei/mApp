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
| Performance | Complete | Cold launch to drawn scene about 2.3 s; paint on the busiest layout 0.5–0.8 ms average under software rendering; dock and camera at 60 Hz | Not measured on Windows or with a large graph |
| Security and privacy | Complete | No network calls, no secrets, no telemetry; reads one bundled file, writes only local settings | — |
| Visual quality | Complete | No placeholder text or debug UI in Release; journey hooks run only under an environment variable | **Fixed:** width-capped paragraphs in the docked inspector were centred; class names broke mid-word. Wireframe previews are schematic by design |
| Testing | Partly complete | Journey ×2 themes, layout fixture, real-input probes, a recorded scenario | The target platform, Windows, has not been exercised |

## Verdict

Complete for its stated scope on Linux, with one accessibility gap (canvas
focus not announced) and one platform gap (no Windows run yet). The larger
product gap is outside the checklist: the graph is authored by hand, so the
app demonstrates the interaction, not extraction.

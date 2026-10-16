# App Orbit: demo cue card

About five minutes. Say the line, then do the action. Never talk while typing.

**Pitch:** Before you change a screen in code you didn't write, App Orbit shows you everything it touches.

**Honest framing:** this is an interaction prototype on a hand-authored graph of a sample app. The question for the room: is this the view you'd want before changing a screen? If yes, the next step is generating the graph from a real app.

**Backup:** `media/app-orbit-scenario.mp4` is a 72-second captioned run of a real triage task, if the live app misbehaves.

**Spine (if you lose your place):** App Orbit lays an app's structure out in space, so you can see how it fits together before you read the code.

## Before you go on

```powershell
git pull
cd AppOrbit\App
dotnet run --project AppOrbit\AppOrbit.csproj -c Release -f net10.0-desktop
```

- [ ] Run **Release**. A Debug build waits for the Hot Design dev server when started outside the IDE.
- [ ] Rehearse once on the demo machine and its display. Every check so far ran on Linux.
- [ ] Display scaling leaves at least 1100 × 640 for the window, which is the app's minimum size.
- [ ] The app remembers its last state. Reset it before going on:
  - Structure lens selected (press `1`).
  - Wire, Flat, Dock and Motion all off.
  - Click **reset layout** at the bottom of the viewer, if it shows.
  - Press `0` to reset the camera, then go up to **Orderly** with `Backspace`.

## Beats

| # | Say | Do | Point at |
|---|---|---|---|
| 1 | "This is App Orbit. It reads an app and lays its structure out in space." | Nothing. Let it sit. | The three feature plates and OrderLineRow underneath |
| 2 | "It's a real 3D camera, so you can turn the app around." | Drag the empty canvas left and right. | The yaw and pitch readout, bottom left |
| 3 | "Zooming changes the level of detail, it doesn't just magnify." | Wheel over **Purchase**, then over **Checkout**. | The Checkout screen card |
| 4 | "The Behavior lens wires every control to the view model member it binds to." | Press `3`. | The binding lines into CartViewModel, and its **shared** badge |
| 5 | "The right column is the text version of the scene, and every claim points at the line it came from." | Scroll the inspector to **Evidence**. | **declared, 100%** and the source pane on the left |
| 6 | "States show what the screen can be in." | Press `4`. | Idle, Submitting, Error |
| 7 | "Search jumps anywhere. A shared component shows every place it's used." | Press `/`. Type `OrderLineRow`. Press Enter. | The three uses across Cart, Checkout and Orders |
| 8 | "One relationship isn't written anywhere in source, so it's marked inferred and explains why." | Press `/`. Type `CartCount`. Press Enter. | The dashed line, and **inferred, 70%** in Evidence |
| 9 | "When you want to read, dock the viewer and the inspector takes the room." | Drag the viewer by its top bar to the bottom right. | The inspector widening |
| 10 | "It's an Uno Platform desktop app: one XAML shell, one SkiaSharp canvas, and every layout checked against the web prototype." | Press `D` to bring the viewer back. | The whole window |

**Close:** "That's App Orbit. Happy to take questions."

## Segues

- 3 → 4: "So that's where things live. Here's how they talk to each other."
- 7 → 8: "Everything so far is declared in source. One thing isn't."
- 8 → 9: "That's a lot to read, so let's make room for it."

## If something goes wrong

| Problem | Do | Say |
|---|---|---|
| The wheel overshoots a level | Press `Backspace`, or click the card and press Enter | "Let me step back one level." |
| The camera ends up at a strange angle | Press `0` | "Back to the default view." |
| The dock drag misses | Press `D` | "There's a key for that." |
| A card gets dragged by accident | Click **reset layout** | "Cards can be moved; this puts them back." |
| Anything else | Press `Backspace` until you're back at **Orderly** and pick up at the next beat | Use the spine line |

## Likely questions

- **"Does it read my app?"** "Today it reads a hand-authored graph of a sample app. Generating that graph from source is the next step."
- **"Is that a live value?"** "Not yet. It's read-only, and the inspector says so: no running app connected."
- **"How do you know the layout is right?"** "The web prototype's layout code runs over every case, and the Uno app is checked against it: 888 cases, zero differences."

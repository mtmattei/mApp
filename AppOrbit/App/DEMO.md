# App Orbit: demo walkthrough

About four minutes, plus questions. Each step is one click or key, one line to say, and what
appears. Say the line, then click. Never talk while typing.

**If you lose your place, say:** "App Orbit shows how an app fits together, before you read its code."
Then press `Backspace` until the top-left reads **Orderly**, and pick up at the next step.

**Backup:** `media/app-orbit-scenario.mp4` is this same story as a 72-second captioned video.

---

## Before you go on (two minutes, off stage)

```powershell
git pull
cd AppOrbit\App
dotnet run --project AppOrbit\AppOrbit.csproj -c Release -f net10.0-desktop
```

- [ ] Use **Release**. A Debug build waits for the Hot Design server when started outside Visual Studio.
- [ ] Rehearse once on the demo machine. Every check so far ran on Linux.
- [ ] The app opens maximized. It needs a 1920 × 1080 display at up to 150% scaling.
- [ ] Reset what the app remembers from rehearsal:
  1. Press `1`. **Structure** is highlighted in the tabs at the top.
  2. **Wire**, **Flat**, **Motion** and **Dock** at the top right are all plain, not highlighted.
  3. If **reset layout** shows at the bottom of the canvas, click it.
  4. Press `0`, then `Backspace` until the top-left reads only **Orderly**.

---

## 1. The hook (15 seconds)

**Say:** "Before you change a screen in code you didn't write, you need to know everything it
touches. App Orbit shows you that, without opening the file tree."

**You see:** the Orderly overview. Three feature panels: Browse, Purchase, Orders. One shared
component, OrderLineRow, underneath.

---

## 2. The story: a bug ticket (about 2½ minutes)

**Say:** "Here's a real kind of ticket. QA says: *Place order stays greyed out on Checkout,
even with items in the cart.* I've never seen this code. Let's find out why."

| # | Do | Say | You see |
|---|---|---|---|
| 1 | Click the title **Purchase** on the middle panel. | "Checkout is part of Purchase." | The Cart and Checkout screens side by side. |
| 2 | Click the **Checkout** screen. | "This is the screen in the ticket." | Checkout up close; its details fill the right panel. |
| 3 | Click **Behavior** in the tabs at the top. | "The Behavior lens shows what every control is wired to." | Lines from the screen to **CartViewModel**, tagged *shared*: Cart uses it too. |
| 4 | On the CartViewModel card, click the **CanPlaceOrder** row. | "Place order is only enabled when CanPlaceOrder is true." | CanPlaceOrder in the middle; the right panel says **depends on 3**. |
| 5 | Point at the right panel's three dependencies. | "Three conditions: items in the cart, an address of at least 8 characters, and no order already submitting. And the source line is already open on the left." | HasItems, Address *Trim().Length >= 8*, IsSubmitting *negated*. Lines 20–22 highlighted on the left. |
| 6 | Click the **Address** card, to the right. | "Where does the address come from?" | **bound by: Address box, Text (TwoWay)**. |
| 7 | Point at the right panel. | "Only the address box feeds it, and nothing on the screen mentions the 8-character rule. That's our bug: the rule works, the user never sees it." | |
| 8 | Press `Alt` + `←`. | "Back to the rule." | CanPlaceOrder again. |
| 9 | Point at the text top-left of the canvas. | "One dependent: only Place order reads this rule. So changing it can't break the Cart screen." | *1 binding · 3 dependencies · 1 dependent*. |
| 10 | Press `D`. | "When I want to read rather than explore, I dock the view, and the details get the room." | The canvas shrinks into a corner; the right panel goes wide. |
| 11 | Press `D` again. | "And back." | The full view returns. |

**Say:** "Diagnosis in about a minute, without opening a file: the fix is a hint under the
address box, and the change is safe for Cart."

---

## 3. One more thing, if there's time (30 seconds)

| # | Do | Say | You see |
|---|---|---|---|
| 12 | Press `/`, type `OrderLineRow`, press `Enter`. | "Search jumps anywhere. A shared component shows every place it's used." | **3 uses**: Cart, Checkout, Orders. |
| 13 | Press `Backspace` until the top-left reads **Orderly**. | "And everything zooms back out." | The overview again. |

---

## 4. Close (15 seconds)

**Say:** "It's an Uno Platform desktop app: one XAML shell, one SkiaSharp canvas, and every
layout checked against the original prototype. Today it reads a hand-written map of a sample
app. The question for you: is this the view you'd want before changing a screen? If yes, the
next step is generating that map from a real app."

---

## If something goes wrong

| Problem | Do | Say |
|---|---|---|
| A click lands one level too deep | Press `Backspace` | "Let me step back one level." |
| You can't find what to click | Press `/`, type its name (`Checkout`, `CanPlaceOrder`, `Address`), press `Enter` | "Search is faster." |
| The view is at a strange angle | Press `0` | "Back to the default view." |
| A card got dragged by accident | Click **reset layout** at the bottom | "Cards can be moved; this puts them back." |
| Anything else | Play `media/app-orbit-scenario.mp4` | "Here's the same thing recorded." |

## Likely questions

- **"Does it read my app?"** "Today it reads a hand-written map of a sample app. Generating that map from source, or from a running app, is the next step."
- **"Does it only work on this sample?"** "No. A second app with a different shape, a field-service app, runs through the same checks, and the layout matches the prototype on every case. It also holds up on a generated app with two hundred screens."
- **"Is that a live value?"** "Not yet. It's read-only, and the panel says so: no running app connected."
- **"How do you know the layout is right?"** "The prototype's own layout code runs over every case, and the Uno app is checked against it: zero differences on both sample apps."

## Keys, if asked

| Key | Does |
|---|---|
| `1` `2` `3` `4` | Structure, Navigation, Behavior, States |
| `/` | Search |
| `Backspace` | Zoom out one level |
| `Alt` + `←` | Back along the trail |
| `D` | Dock or expand the view |
| `0` | Reset the camera |
| `?` | All shortcuts |

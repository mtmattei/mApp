// Journey test for the App Orbit prototype. Drives the page through real clicks, wheel and keys.
//   1. Orientation: application → Purchase → Checkout → back out without losing place.
//   2. Relationship tracing: a route, a binding to a view model member, the uses of a shared component.
//   3. Inspection: details, evidence with a source reference, runtime marked unavailable, open source.
// Also exercises the flat view, docked mode and the reduced-motion path, and saves screenshots.
//
// Run:  node tests/smoke.mjs            (expects http://localhost:8787, see serve.ps1 / serve.sh)
//       APP_ORBIT_URL=http://localhost:5000 node tests/smoke.mjs
// Needs the playwright package (npm i -D playwright, or a global install on PLAYWRIGHT_MODULE).

import { mkdirSync } from 'node:fs';

const modulePath = process.env.PLAYWRIGHT_MODULE || 'playwright';
const { chromium } = await import(modulePath);
const url = process.env.APP_ORBIT_URL || 'http://localhost:8787/index.html';
const outDir = new URL('./screenshots/', import.meta.url).pathname;
mkdirSync(outDir, { recursive: true });

const failures = [];
const check = (cond, msg) => { if (!cond) failures.push(msg); console.log(`${cond ? 'ok  ' : 'FAIL'} ${msg}`); };

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
await page.goto(url);
await page.waitForFunction(() => window.appOrbit && document.querySelectorAll('.card').length > 0);

const state = () => page.evaluate(() => { const s = window.appOrbit.store.get(); return { focusId: s.focusId, lens: s.lens, view: s.view, mode: s.mode }; });
const crumb = () => page.locator('#breadcrumb').innerText();
const shot = (name) => page.screenshot({ path: `${outDir}${name}.png` });
const settle = () => page.waitForTimeout(500);

// ---- 1. Orientation ----
check((await page.locator('.card[data-type="feature"]').count()) === 3, 'application level shows 3 features');
check((await page.locator('.plate-screen').count()) === 5, 'application level shows 5 screens');
await shot('01-application');
await page.locator('.card[data-key="feature.purchase"] .card-head').click();
await settle();
check((await state()).focusId === 'feature.purchase', 'click a feature plate → feature level');
check((await crumb()).includes('Purchase'), 'breadcrumb shows Purchase');
await shot('02-feature-purchase');
await page.locator('.card[data-key="screen.checkout"] .card-head').click();
await settle();
check((await state()).focusId === 'screen.checkout', 'click a screen → screen level');
check(/Orderly[\s\S]*Purchase[\s\S]*Checkout/.test(await crumb()), 'breadcrumb reads Orderly › Purchase › Checkout');
await page.keyboard.press('Minus');
await settle();
check((await state()).focusId === 'feature.purchase', '− zooms out to the feature');
await page.keyboard.press('Minus');
await settle();
check((await state()).focusId === null, '− again returns to the application');

// semantic zoom with the wheel: zoom in over Checkout's thumbnail inside the Purchase plate
await page.locator('.card[data-key="feature.purchase"] .card-head').click();
await settle();
const checkoutCard = page.locator('.card[data-key="screen.checkout"]');
await checkoutCard.hover();
for (let i = 0; i < 6; i++) { await page.mouse.wheel(0, -120); await page.waitForTimeout(40); }
await settle();
check((await state()).focusId === 'screen.checkout', 'wheel past the threshold over a card zooms into it');
check(Math.abs((await page.evaluate(() => window.appOrbit.scene.getCamera().scale)) - 1) < 0.01, 'the semantic jump resets the continuous scale to 100%');

// ---- 2. Relationship tracing ----
await page.keyboard.press('2');
await settle();
check((await page.locator('.card[data-key="route.cart-to-checkout#in"]').count()) === 1, 'navigation lens: incoming route from Cart');
check((await page.locator('.card[data-key="route.checkout-to-orders#out"]').count()) === 1, 'navigation lens: outgoing route to Orders');
check((await page.locator('#links path.route').count()) === 2, 'two route connectors drawn');
await shot('03-checkout-navigation');
await page.locator('.card[data-key="route.checkout-to-orders#out"] .face').click();
await settle();
check((await state()).focusId === 'screen.orders', 'following the outgoing route lands on Orders');
await page.keyboard.press('Backspace');
await settle();
check((await state()).focusId === 'feature.orders', 'Backspace zooms out to the Orders feature (parent, not history)');
await page.locator('#viewer-back').click();
await settle();
await page.locator('#viewer-back').click();
await settle();
check((await state()).focusId === 'screen.checkout', 'the back button retraces the trail to Checkout');

await page.keyboard.press('3');
await settle();
check((await page.locator('.card[data-key="vm.cart"]').count()) === 1, 'behavior lens: CartViewModel plane is shown');
check((await page.locator('.card[data-key="vm.cart"] .tag.shared').count()) === 1, 'CartViewModel is tagged shared');
const bindLinks = await page.locator('#links path.binds-to, #links path.invokes').count();
check(bindLinks === 8, `seven bindings and one command connector (${bindLinks})`);
await shot('04-checkout-behavior');

await page.locator('.region[data-id="inst.checkout.place-order"]').click();
await settle();
check((await state()).focusId === 'inst.checkout.place-order', 'click Place order in the preview → component level');
check((await page.locator('.member.hot').count()) === 3, 'three view model members light up for Place order');
const hotNames = await page.locator('.member.hot .mname').allInnerTexts();
check(hotNames.includes('CanPlaceOrder') && hotNames.includes('PlaceOrder'), 'they include CanPlaceOrder and PlaceOrder');
await shot('05-place-order-behavior');

await page.locator('.member[data-id="prop.cart.can-place-order"]').click();
await settle();
check((await state()).focusId === 'prop.cart.can-place-order', 'click a member → detail level');
check((await page.locator('.card[data-key="prop.cart.has-items#out"]').count()) === 1, 'CanPlaceOrder reads HasItems');
check((await page.locator('.card[data-key="state.place-order.disabled#in"]').count()) === 1, 'the Disabled state depends on it');
await shot('06-can-place-order');

// shared component: find every use
await page.locator('#search-input').fill('OrderLineRow');
await page.keyboard.press('Enter');
await settle();
check((await state()).focusId === 'component.order-line-row', 'search + Enter focuses the OrderLineRow definition');
const useScreens = await page.locator('.card[data-type="screen"] .card-head .name').allInnerTexts();
check(['Cart', 'Checkout', 'Orders'].every((n) => useScreens.includes(n)), `definition view shows uses on Cart, Checkout, Orders (${useScreens.join(', ')})`);
await shot('07-order-line-row-uses');

// ---- 3. Inspection ----
await page.evaluate(() => window.appOrbit.focus('prop.cart.can-place-order'));
await settle();
const inspector = await page.locator('#inspector').innerText();
check(inspector.includes('IFeed<bool>'), 'inspector shows the declared type');
check(inspector.includes('Live values unavailable'), 'inspector marks runtime values unavailable');
check(inspector.includes('CartModel.cs:20'), 'inspector shows the source reference');
check(inspector.includes('declared'), 'inspector shows evidence kind');
await page.locator('#inspector .actions button', { hasText: 'Open source' }).click();
await settle();
check((await page.locator('#editor-code .line.at').innerText()).includes('CanPlaceOrder'), 'Open source moves the editor to the CanPlaceOrder line');
// editor → viewer
await page.locator('#editor-code .line[data-line="27"]').click();
await settle();
check((await state()).focusId === 'cmd.cart.place-order', 'clicking a source line focuses the entity declared there');
// inferred relationship is marked
await page.evaluate(() => window.appOrbit.focus('prop.catalog.cart-count'));
await settle();
check((await page.locator('#inspector .tag.inferred').count()) >= 1, 'inferred relationship is tagged in the inspector');
check((await page.locator('#links path.inferred').count()) === 1, 'inferred relationship is drawn dashed');
await shot('08-inferred');

// ---- fidelity toggle and card dragging ----
await page.evaluate(() => window.appOrbit.focus('screen.checkout'));
await page.keyboard.press('3');
await page.keyboard.press('w');
await settle();
check((await page.locator('.preview.ui').count()) >= 1, 'W switches previews to UI fidelity');
check((await page.locator('.card[data-key="screen.checkout"] .item-name').first().innerText()).includes('Flat white'), 'UI previews show the sample rows from the graph');
await page.keyboard.press('w');
await settle();
check((await page.locator('.preview.wire').count()) >= 1, 'W again returns to wireframes');
{
  const head = page.locator('.card[data-key="vm.cart"] .card-head');
  const b0 = await head.boundingBox();
  const cx = b0.x + b0.width / 2, cy = b0.y + b0.height / 2;
  await page.mouse.move(cx, cy); await page.mouse.down();
  await page.mouse.move(cx + 40, cy + 20, { steps: 5 }); await page.mouse.move(cx + 120, cy + 60, { steps: 10 }); await page.mouse.up();
  await settle();
  const b1 = await head.boundingBox();
  check(b1.x - b0.x > 80 && b1.y - b0.y > 30, `dragging a card moves it (${Math.round(b1.x - b0.x)}, ${Math.round(b1.y - b0.y)})`);
  check((await state()).focusId === 'screen.checkout', 'dragging does not change the focus');
  check(await page.locator('#layout-reset').isVisible(), 'reset layout appears after a move');
  await page.locator('#layout-reset').click();
  await settle();
  const b2 = await head.boundingBox();
  check(Math.abs(b2.x - b0.x) < 1 && Math.abs(b2.y - b0.y) < 1, 'reset layout puts the card back');
}

// ---- flat view, docked mode, reduced motion, keyboard ----
await page.evaluate(() => window.appOrbit.focus('screen.checkout'));
await page.keyboard.press('3');
await page.keyboard.press('f');
await settle();
check((await state()).view === 'flat', 'F switches to the flat view');
check((await page.locator('#links path.binds-to, #links path.invokes').count()) === 8, 'flat view keeps the same connectors');
const transform = await page.locator('#world').evaluate((el) => el.style.transform);
check(/rotateX\(0deg\) rotateY\(0deg\)/.test(transform), 'flat view has no rotation');
await shot('09-flat');
await page.keyboard.press('f');
await page.keyboard.press('d');
await settle();
check((await state()).mode === 'docked', 'D docks the viewer');
check((await state()).focusId === 'screen.checkout', 'docking keeps the focus');
const box = await page.locator('#viewer').boundingBox();
check(box.width < 420 && box.height < 300, 'docked viewer is a small panel');
await shot('10-docked');
await page.keyboard.press('d');
await settle();

await page.locator('#toggle-motion').click();
await page.evaluate(() => window.appOrbit.focus('screen.cart'));
const enterClass = await page.locator('#world').evaluate((el) => el.classList.contains('enter'));
check(!enterClass, 'reduced motion: no crossfade animation on focus change');
await page.locator('#toggle-motion').click();

// keyboard: Tab to a card, Enter focuses it
await page.evaluate(() => window.appOrbit.focus(null));
await settle();
await page.locator('#viewer').focus();
await page.keyboard.press('Tab');
await page.keyboard.press('Enter');
await settle();
check((await state()).focusId !== null, 'Tab + Enter focuses the first card from the keyboard');

check(errors.length === 0, `no console errors (${errors.join(' | ')})`);
await browser.close();
console.log(failures.length ? `\n${failures.length} failure(s)` : '\nall checks passed');
process.exit(failures.length ? 1 : 0);

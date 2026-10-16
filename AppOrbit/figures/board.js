/**
 * Board: an app's screens as the keys of a small board, one row per feature,
 * the rows staggered and stepped as a real board's are, each cap narrower at
 * its top than at its foot. The entry screen carries the homing bar. The key
 * under the pointer sinks all the way, and the screens it navigates to follow
 * it down, less with every hop along the routes, each on its own spring. At
 * rest the entry key is caught half-way down, bright. The slider is how far
 * along the routes a press carries, in hops.
 *
 * The pattern: a field over discrete parts, as Keyboard's, but the distance
 * is counted along the app's routes, not across the board.
 */
const { Cam, clamp, facing, fit, hull, open, poly, prism, proj, ringAt, rings, rrect, run, seg, unproj, spring, stepS, disposer, mk, pointer, put, register, solid } = HL;

const U = 22, PAD = 1, TAPER = 2.4, Z0 = 1, TRAVEL = 7, BZ = 7, CASE = 9, W = 4;
/** Each row's rest height, back to front. */
const ROW_H = [12.4, 11, 12];
/** The screens, row by row: [width in keys, name]. */
const ROWS = [
  [[2, "catalog"], [2, "product"]],
  [[2, "cart"], [2, "checkout"]],
  [[4, "orders"]],
];
const ENTRY = "catalog";
/** The routes: where each screen can go. */
const ROUTES = { catalog: ["product", "cart"], product: ["cart"], cart: ["checkout"], checkout: ["orders"], orders: ["cart"] };

/** How far a key follows a press, h hops down the routes from it, with the press carrying R hops. */
const follow = (h, R) => clamp(1 - h / (R + 1), 0, 1) * 0.7;

function mount({ stage, svg, read }, value) {
  const bag = disposer();
  const C = Cam(45, 0.5, 2.35);
  fit(C, [[-BZ, -BZ, -CASE], [W * U + BZ, 3 * U + BZ, -CASE], [W * U + BZ, -BZ, -CASE], [-BZ, 3 * U + BZ, -CASE], [0, 0, ROW_H[0]]], 200, 166);
  const P = proj(C), front = facing(C);
  let R = value, over = null, lit = null;

  const g = mk("g", {}, svg);
  const [cr, ci] = rings(-BZ, -BZ, W * U + BZ, 3 * U + BZ, 8, 2.2);
  put(solid(g), prism(P, front, cr, ci, -CASE, 0));

  // back to front, left to right: each key is cut from the ones behind and to its left
  const keys = [];
  ROWS.forEach((row, r) => {
    let x = 0;
    for (const [w, name] of row) {
      const x0 = x * U, x1 = (x + w) * U, y0 = r * U, y1 = (r + 1) * U, t = PAD + TAPER;
      const el = solid(g);
      keys.push({
        r, name, x0, x1, y0, y1, h: ROW_H[r],
        foot: rrect(x0 + PAD, y0 + PAD, x1 - PAD, y1 - PAD, 3.4, 4),
        top: rrect(x0 + t, y0 + t, x1 - t, y1 - t, 2.6, 4),
        inner: rrect(x0 + t + 1.1, y0 + t + 1.1, x1 - t - 1.1, y1 - t - 1.1, 1.6, 4),
        el, sp: spring(ROW_H[r], { eps: 0.02 }), drawn: NaN,
        bump: name === ENTRY ? mk("path", { class: "lo nf" }, el.g) : null,
      });
      x += w;
    }
  });
  const byName = Object.fromEntries(keys.map((k) => [k.name, k]));
  const entry = byName[ENTRY];

  /** Hops from one screen to every other along the routes: a breadth-first walk. */
  function hops(from) {
    const d = { [from]: 0 }, q = [from];
    while (q.length) { const s = q.shift(); for (const t of ROUTES[s] || []) if (!(t in d)) { d[t] = d[s] + 1; q.push(t); } }
    return d;
  }
  /** Each key's target depth from a press on `pressed`, carrying `radius` hops, at a share of full travel. */
  function sink(pressed, radius, depth) {
    const d = hops(pressed.name);
    for (const k of keys) {
      const f = k === pressed ? 1 : k.name in d ? follow(d[k.name], radius) : 0;
      k.sp.t = k.h - TRAVEL * depth * f;
    }
  }
  sink(entry, 1, 0.55);
  for (const k of keys) k.sp.x = k.sp.t;

  function drawKey(k) {
    const h = k.sp.x;
    if (h === k.drawn) return;
    k.drawn = h;
    put(k.el, { sil: poly(hull(ringAt(P, k.foot, Z0).concat(ringAt(P, k.top, h)))), crease: open(ringAt(P, run(k.inner, front), h)) });
    if (k.bump) {
      const cx = (k.x0 + k.x1) / 2, cy = k.y1 - PAD - TAPER - 3.6;
      k.bump.setAttribute("d", seg(P(cx - 3.2, cy, h), P(cx + 3.2, cy, h)));
    }
  }
  function light(k) {
    if (k === lit) return;
    if (lit) lit.el.sil.classList.remove("hi");
    lit = k;
    k.el.sil.classList.add("hi");
  }

  const B = register(stage, (dt) => {
    let m = false;
    for (const k of keys) { if (stepS(k.sp, dt)) m = true; drawKey(k); }
    return m;
  });
  bag.add(B.unregister);

  const keyAt = (r, x) => keys.find((k) => k.r === r && x >= k.x0 && x < k.x1);
  /** The key under a screen point, tested against each row's rest top, front row first. */
  function hit(p) {
    for (let r = ROWS.length - 1; r >= 0; r--) {
      const q = unproj(C, p[0], p[1], ROW_H[r]);
      if (q[1] < r * U || q[1] >= (r + 1) * U || q[0] < 0 || q[0] >= W * U) continue;
      return keyAt(r, q[0]);
    }
    const q = unproj(C, p[0], p[1], ROW_H[1]);
    if (q[0] < -BZ || q[0] > W * U + BZ || q[1] < -BZ || q[1] > 3 * U + BZ) return null;
    return keyAt(Math.floor(clamp(q[1], 0, 3 * U - 0.01) / U), clamp(q[0], 0, W * U - 0.01));
  }

  function retarget() {
    if (over) { sink(over, R, 1); light(over); read.textContent = over.name; }
    else { sink(entry, 1, 0.55); light(entry); read.textContent = "rest"; }
    B.wake();
  }
  light(entry);

  bag.add(pointer(stage, {
    move: (p) => { over = hit(p); retarget(); },
    leave: () => { over = null; retarget(); },
  }));
  bag.add(() => svg.replaceChildren());

  return {
    set: (v) => { R = v; if (over) retarget(); },
    destroy: bag.dispose,
  };
}

hairline({
  name: "board",
  means: "An app's screens as keys, one row per feature; the one under the pointer sinks and the screens it leads to follow, a hop less each.",
  rules: [1, 3, 4, 5, 9],
  range: [0.4, 1.2, 2.6],
  mount,
});

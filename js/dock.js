// Docking: pick the viewer up by its head bar, carry it, feel the pull of a home, let it settle.
// Two homes: the main column (expanded) and the dock slot (docked). The panel never floats free.
// All motion is springs on one frame loop; reduced motion resolves each change in a step.

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const smooth = (t) => { t = clamp(t, 0, 1); return t * t * (3 - 2 * t); };

function spring(x, k, c) { return { x, v: 0, t: x, k, c }; }
function step(sp, dt) {
  const n = Math.max(1, Math.ceil(dt * 240)), h = dt / n;
  for (let i = 0; i < n; i++) { const a = -sp.k * (sp.x - sp.t) - sp.c * sp.v; sp.v += a * h; sp.x += sp.v * h; }
  if (Math.abs(sp.x - sp.t) < 0.3 && Math.abs(sp.v) < 2) { sp.x = sp.t; sp.v = 0; return false; }
  return true;
}

/**
 * @param els { shell, viewer, head, slot, ghost }
 * @param api { getMode(), setMode(mode), reducedMotion() }
 */
export function createDock(els, api) {
  const { shell, viewer, head, slot, ghost } = els;
  const POS = { k: 190, c: 26 };   // position: stiff, a touch under critical so it lands with weight
  const SIZE = { k: 240, c: 30 };  // size: slightly stiffer, no visible overshoot
  const rect = { x: spring(0, POS.k, POS.c), y: spring(0, POS.k, POS.c), w: spring(0, SIZE.k, SIZE.c), h: spring(0, SIZE.k, SIZE.c) };
  let phase = 'resting'; // resting | lifted | flying
  let drag = null;       // { px, py, gx, gy, origin, moved, pointerId }
  let target = null;     // home we are flying to
  let raf = 0, last = 0;
  let preview = null;    // home whose ghost is showing

  // ---------- homes ----------
  const num = (name) => parseFloat(getComputedStyle(shell).getPropertyValue(name)) || 0;
  function homes() {
    const s = shell.getBoundingClientRect();
    const sl = slot.getBoundingClientRect();
    const w = num('--dock-w'), h = num('--dock-h'), gap = num('--dock-gap'), insp = num('--inspector-w');
    return {
      expanded: { x: sl.left - s.left, y: sl.top - s.top, w: sl.width, h: sl.height, reach: 0 },
      docked: { x: s.width - insp - gap - w, y: s.height - gap - h, w, h, reach: 210 },
      bounds: { w: s.width, h: s.height },
    };
  }
  const centre = (r) => ({ x: r.x + r.w / 2, y: r.y + r.h / 2 });
  const dist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);

  // ---------- placing ----------
  function apply() {
    viewer.style.left = `${rect.x.x}px`;
    viewer.style.top = `${rect.y.x}px`;
    viewer.style.width = `${rect.w.x}px`;
    viewer.style.height = `${rect.h.x}px`;
  }
  function snapTo(home) {
    for (const [k, v] of [['x', home.x], ['y', home.y], ['w', home.w], ['h', home.h]]) { rect[k].x = v; rect[k].t = v; rect[k].v = 0; }
    apply();
  }
  /** Puts the viewer on its current home without motion (boot, resize). */
  function place() {
    if (phase !== 'resting') return;
    snapTo(homes()[api.getMode()]);
  }

  function showGhost(home) {
    if (!home) { ghost.classList.remove('show', 'hard'); preview = null; return; }
    ghost.style.left = `${home.x}px`; ghost.style.top = `${home.y}px`; ghost.style.width = `${home.w}px`; ghost.style.height = `${home.h}px`;
    ghost.classList.add('show');
  }

  // ---------- loop ----------
  function loop() {
    if (raf) return;
    last = performance.now();
    const tick = (now) => {
      const dt = Math.min(0.05, (now - last) / 1000); last = now;
      let moving = false;
      for (const k of ['x', 'y', 'w', 'h']) if (step(rect[k], dt)) moving = true;
      apply();
      if (phase === 'flying' && !moving) { settle(); raf = 0; return; }
      raf = moving || phase === 'lifted' ? requestAnimationFrame(tick) : 0;
    };
    raf = requestAnimationFrame(tick);
  }
  function retarget(x, y, w, h) { rect.x.t = x; rect.y.t = y; rect.w.t = w; rect.h.t = h; }

  // ---------- phases ----------
  function lift(e) {
    const H = homes();
    const origin = api.getMode();
    const from = H[origin];
    phase = 'lifted';
    viewer.classList.add('floating', 'lifted');
    const s = shell.getBoundingClientRect();
    const px = e.clientX - s.left, py = e.clientY - s.top;
    // the grab point, as a share of the panel, stays under the pointer while the size changes
    drag = { px, py, gx: clamp((px - from.x) / from.w, 0.05, 0.95), gy: clamp((py - from.y) / from.h, 0, 1), origin, pointerId: e.pointerId };
    snapTo(from);
    const carry = { w: H.docked.w * 1.03, h: H.docked.h * 1.03 };
    retarget(px - drag.gx * carry.w, py - drag.gy * carry.h, carry.w, carry.h);
    if (api.reducedMotion()) { snapTo({ x: rect.x.t, y: rect.y.t, w: carry.w, h: carry.h }); }
    showGhost(from); ghost.classList.add('holder');
    preview = null;
    api.setCarrying(true);
    loop();
  }

  function carry(e) {
    const s = shell.getBoundingClientRect();
    const px = e.clientX - s.left, py = e.clientY - s.top;
    drag.px = px; drag.py = py;
    const H = homes();
    const w = rect.w.t, h = rect.h.t;
    // the panel stays inside the shell: a tool cannot be carried through the bench
    let x = clamp(px - drag.gx * w, 0, H.bounds.w - w), y = clamp(py - drag.gy * h, 0, H.bounds.h - h);
    // which home is in reach: the dock slot by distance (or the pointer inside it), the column by the pointer being over it
    const inside = (r, qx, qy) => qx >= r.x && qx <= r.x + r.w && qy >= r.y && qy <= r.y + r.h;
    const toDock = dist({ x: x + w / 2, y: y + h / 2 }, centre(H.docked));
    let home = null;
    if (toDock < H.docked.reach || inside(H.docked, px, py)) home = 'docked';
    else if (inside(H.expanded, px, py) && drag.origin === 'docked') home = 'expanded';
    if (home === 'docked') {
      // the magnet: alignment blends in as the slot comes near, but the hand stays in charge
      const pull = smooth((H.docked.reach - toDock) / H.docked.reach) * 0.55;
      x += (H.docked.x - x) * pull; y += (H.docked.y - y) * pull;
    }
    retarget(x, y, w, h);
    if (api.reducedMotion()) snapTo({ x, y, w, h });
    if (home !== preview) {
      preview = home;
      // no home in reach: the holder you lifted it from stays drawn, so there is always a place it belongs
      ghost.classList.toggle('holder', !home);
      showGhost(home ? H[home] : H[drag.origin]);
      // the layout answers before the drop: the scene and the columns take the destination's shape
      api.setMode(home || drag.origin);
      // homes moved with the grid: refresh the ghost after the layout settles
      requestAnimationFrame(() => { if (drag) showGhost(homes()[preview || drag.origin]); });
    }
    loop();
  }

  function release() {
    const home = preview || drag.origin;
    drag = null;
    flyTo(home);
  }

  /** Flies the viewer to a home along the springs, then settles there. Also the D key's path. */
  function flyTo(home) {
    const H = homes();
    target = home;
    phase = 'flying';
    viewer.classList.add('floating');
    viewer.classList.remove('lifted');
    api.setMode(home);
    requestAnimationFrame(() => {
      const h = homes()[home];
      retarget(h.x, h.y, h.w, h.h);
      showGhost(h); ghost.classList.add('hard');
      if (api.reducedMotion()) { snapTo(h); settle(); return; }
      loop();
    });
    void H;
  }

  function settle() {
    phase = 'resting';
    api.setCarrying(false);
    viewer.classList.remove('floating', 'lifted');
    viewer.style.left = viewer.style.top = viewer.style.width = viewer.style.height = '';
    showGhost(null);
    ghost.classList.remove('hard', 'holder');
    place();
    if (!api.reducedMotion()) {
      viewer.classList.add('settled');
      setTimeout(() => viewer.classList.remove('settled'), 180);
    }
    target = null;
  }

  // ---------- input ----------
  head.addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || e.target.closest('button, input, a')) return;
    if (phase === 'flying') return;
    drag = { pending: true, sx: e.clientX, sy: e.clientY, pointerId: e.pointerId, start: e };
    head.setPointerCapture(e.pointerId);
  });
  head.addEventListener('pointermove', (e) => {
    if (!drag) return;
    if (drag.pending) {
      if (Math.hypot(e.clientX - drag.sx, e.clientY - drag.sy) < 4) return;
      lift(drag.start);
    }
    if (phase === 'lifted') carry(e);
  });
  const end = () => { if (!drag) return; if (drag.pending) { drag = null; return; } release(); };
  head.addEventListener('pointerup', end);
  head.addEventListener('pointercancel', end);

  new ResizeObserver(() => place()).observe(slot);
  new ResizeObserver(() => place()).observe(shell);

  return {
    place,
    toggle() { if (phase === 'resting') flyTo(api.getMode() === 'docked' ? 'expanded' : 'docked'); },
    isMoving: () => phase !== 'resting',
  };
}

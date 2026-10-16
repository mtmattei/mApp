/**
 * Orbit: one app screen taken apart the way App Orbit reads it. Four plates on
 * one axis: the routes it sits on, the view model behind it, its states, and
 * the UI on top, each with the marks that make it what it is: a lane with a
 * pad at each end, rows of members with one ringed, three thumbnails of the
 * same screen, and the screen itself with its list, its field and its button.
 * The pointer's x scrubs the gap through a spring; its y picks a plate, which
 * takes the bright edge from the button. One dashed thread drops from the
 * button to the member it is bound to, and the plates between hide it.
 * The slider is the gap, in window units.
 *
 * The pattern: scrub and pick, as Exploded's. Picking is tested against the
 * target gap, never the gap on screen.
 */
const { Cam, circ, clamp, extremes, facing, fit, poly, prism, proj, ringAt, rings, rrect, seg, spring, stepS, disposer, mk, pointer, put, register, solid } = HL;

const W = 132, H = 96, TK = 2.4, REST = 0.32;
/** Bottom to top. r: footprint; segs: lines on the top face; boxes: outlines on it, with a class; pads: rings on it. */
const LAY = [
  { name: "routes", r: [0, 0, W, H], rad: 7, segs: [[[22, 48], [50, 48]], [[82, 48], [110, 48]]], pads: [[14, 48], [118, 48]], boxes: [[52, 30, 80, 66, 4, "nf lo"]] },
  { name: "behavior", r: [10, 10, 122, 86], rad: 5, segs: [[[18, 22], [60, 22]], [[18, 32], [84, 32]], [[18, 42], [52, 42]], [[18, 62], [72, 62]], [[18, 72], [96, 72]]], boxes: [[15, 47, 117, 57, 2, "nf sil"]] },
  { name: "states", r: [4, 4, 128, 92], rad: 6, boxes: [[12, 18, 42, 78, 3, "nf"], [51, 18, 81, 78, 3, "nf"], [90, 18, 120, 78, 3, "nf"], [16, 66, 38, 73, 2, "nf"], [55, 66, 77, 73, 2, "nf lo"], [94, 66, 116, 73, 2, "nf"]],
    segs: [[[16, 24], [30, 24]], [[55, 24], [69, 24]], [[94, 24], [108, 24]]] },
  { name: "ui", r: [0, 0, W, H], rad: 7, segs: [[[10, 12], [58, 12]], [[25, 25], [92, 25]], [[25, 39], [78, 39]]],
    boxes: [[10, 20, 20, 30, 2, "nf"], [10, 34, 20, 44, 2, "nf"], [10, 52, 122, 63, 3, "nf"], [10, 73, 122, 86, 4, "nf sil"]] },
];
const BUTTON = 3, BIND = 1; // the button's box on the ui plate, and the ringed member row on the behavior plate

function inside(pt, pg) {
  let c = false;
  for (let i = 0, j = pg.length - 1; i < pg.length; j = i++) {
    const [xi, yi] = pg[i], [xj, yj] = pg[j];
    if ((yi > pt[1]) !== (yj > pt[1]) && pt[0] < ((xj - xi) * (pt[1] - yi)) / (yj - yi) + xi) c = !c;
  }
  return c;
}

function mount({ stage, svg, read }, value) {
  const bag = disposer();
  let GAP = value, act = -1, lastP = null;
  const e = spring(REST, { eps: 0.002 });
  const C = Cam(45, 0.5, 1.7);
  fit(C, [[0, 0, 0], [W, H, 0], [W, 0, 0], [0, H, 0], [0, 0, 3 * 34 + TK], [W, 0, 3 * 34 + TK]], 190, 166);
  const P = proj(C), front = facing(C);
  const g = mk("g", {}, svg);

  const els = LAY.map((L, i) => {
    const [ring, inner] = rings(...L.r, L.rad, 1.3);
    const ext = extremes(P, ring);
    // guides go in before the plate they belong to: paint order hides them
    const guide = i > 0 ? mk("path", { class: "nf dash" }, g) : null;
    const thread = i === BIND + 1 ? mk("path", { class: "nf dash" }, g) : null;
    const el = solid(g);
    const marks = mk("path", { class: "nf" }, el.g);
    const boxes = (L.boxes || []).map(([x0, y0, x1, y1, r, cls]) => ({ ring: rrect(x0, y0, x1, y1, r), el: mk("path", { class: cls }, el.g) }));
    const pads = (L.pads || []).map(() => mk("path", { class: "nf" }, el.g));
    return { ring, inner, ext, guide, thread, el, marks, boxes, pads };
  });
  const button = els[3].boxes[BUTTON].el;
  const pad = circ(5, 24);

  const corners = ([x0, y0, x1, y1], z) => [P(x0, y0, z), P(x1, y0, z), P(x1, y1, z), P(x0, y1, z)];
  /** The topmost plate under the pointer, tested where the plates are going, not where they are. */
  function pick(p) {
    if (!p) return -1;
    for (let i = LAY.length - 1; i >= 0; i--) if (inside(p, corners(LAY[i].r, i * GAP * e.t + TK))) return i;
    return -1;
  }
  function setAct(a) {
    if (a === act) return;
    act = a;
    els.forEach((E, i) => E.el.sil.classList.toggle("hi", i === a));
    button.classList.toggle("hi", a < 0);
    B.wake();
  }

  const B = register(stage, (dt) => {
    const m = stepS(e, dt), z = (i) => i * GAP * e.x;
    LAY.forEach((L, i) => {
      const E = els[i], zi = z(i), zt = zi + TK;
      put(E.el, prism(P, front, E.ring, E.inner, zi, zt));
      E.marks.setAttribute("d", (L.segs || []).map(([a, b]) => seg(P(a[0], a[1], zt), P(b[0], b[1], zt))).join(""));
      E.boxes.forEach((bx) => bx.el.setAttribute("d", poly(ringAt(P, bx.ring, zt))));
      (L.pads || []).forEach(([px, py], k) => E.pads[k].setAttribute("d", poly(pad.map((q) => P(px + q.u, py + q.v, zt)))));
      if (E.guide) {
        const zp = z(i - 1) + TK;
        E.guide.setAttribute("d", E.ext.map((q) => seg(P(q.u, q.v, zi), P(q.u, q.v, zp))).join(""));
      }
      if (E.thread) {
        // from the button's centre on the ui plate's underside down to the ringed row on the behavior plate
        const [bx0, by0, bx1, by1] = LAY[3].boxes[BUTTON], [rx0, ry0, rx1, ry1] = LAY[BIND].boxes[0];
        E.thread.setAttribute("d", seg(P((bx0 + bx1) / 2, (by0 + by1) / 2, z(3)), P((rx0 + rx1) / 2, (ry0 + ry1) / 2, z(BIND) + TK)));
      }
    });
    read.textContent = act >= 0 ? `0${LAY.length - act} · ${LAY[act].name} · z ${z(act).toFixed(1)}` : "rest";
    return m;
  });
  bag.add(B.unregister);
  setAct(-1);

  bag.add(pointer(stage, {
    move: (p) => { lastP = p; e.t = REST + (1 - REST) * clamp((p[0] - 60) / 280, 0, 1); setAct(pick(p)); B.wake(); },
    leave: () => { lastP = null; e.t = REST; setAct(-1); B.wake(); },
  }));
  bag.add(() => svg.replaceChildren());

  return {
    set: (v) => { GAP = v; if (lastP) setAct(pick(lastP)); B.wake(); },
    destroy: bag.dispose,
  };
}

hairline({
  name: "orbit",
  means: "A screen taken apart into its routes, view model, states and UI; moving across opens the gap, moving down picks a layer.",
  rules: [1, 3, 4, 6, 8],
  range: [8, 20, 34],
  mount,
});

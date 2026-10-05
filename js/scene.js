// DOM scene: cards on CSS 3D planes, a camera with clamps, and screen-space SVG connectors
// drawn between anchor elements. Links read anchor rects back from layout each frame.

import * as G from './graph.js';
import { renderPreview, addAnchors } from './preview.js';
import { FRAME } from './layout.js';

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const YAW_MAX = 40, PITCH_MAX = 22;
const DEFAULT_CAM = { orbit: { yaw: -18, pitch: 6 }, flat: { yaw: 0, pitch: 0 } };

const h = (tag, cls, text) => {
  const el = document.createElement(tag);
  if (cls) el.className = cls;
  if (text != null) el.textContent = text;
  return el;
};

export function createScene(els, handlers) {
  const { viewer, stage, world, svg } = els;
  const cam = { yaw: -18, pitch: 6, scale: 1 };
  const target = { ...cam };
  let fit = { scale: 1, bx: 0, by: 0 };
  let current = null; // { state, layout }
  let raf = 0;
  let suppressClick = false;

  // ---------- camera ----------
  function setCamera(patch, immediate) {
    Object.assign(target, patch);
    target.yaw = clamp(target.yaw, -YAW_MAX, YAW_MAX);
    target.pitch = clamp(target.pitch, -PITCH_MAX, PITCH_MAX);
    target.scale = clamp(target.scale, 0.45, 2.4);
    if (immediate || current?.state.reducedMotion) Object.assign(cam, target), apply();
    else loop();
  }
  const getCamera = () => ({ ...target });
  function resetCamera(view) {
    setCamera({ ...DEFAULT_CAM[view || current?.state.view || 'orbit'], scale: 1 });
  }
  function loop() {
    if (raf) return;
    const step = () => {
      let done = true;
      for (const k of ['yaw', 'pitch', 'scale']) {
        const d = target[k] - cam[k];
        if (Math.abs(d) > (k === 'scale' ? 0.002 : 0.05)) { cam[k] += d * 0.18; done = false; } else cam[k] = target[k];
      }
      apply();
      raf = done ? 0 : requestAnimationFrame(step);
    };
    raf = requestAnimationFrame(step);
  }
  function apply() {
    const r = stage.getBoundingClientRect();
    const cx = r.width / 2, cy = r.height / 2;
    const S = fit.scale * cam.scale;
    world.style.transform = `translate3d(${cx}px, ${cy}px, 0) scale(${S}) rotateX(${cam.pitch}deg) rotateY(${cam.yaw}deg) translate3d(${-fit.bx}px, ${-fit.by}px, 0)`;
    drawLinks();
    handlers.onCamera?.(cam, S);
  }
  function computeFit() {
    const r = stage.getBoundingClientRect();
    const b = current.layout.bounds;
    const padX = current.state.mode === 'docked' ? 24 : 90;
    const padY = current.state.mode === 'docked' ? 40 : 110;
    const s = Math.min((r.width - padX) / Math.max(b.w, 1), (r.height - padY) / Math.max(b.h, 1), 1);
    fit = { scale: Math.max(s, 0.25), bx: b.x, by: b.y };
  }

  // ---------- render ----------
  function render(state, layout) {
    const prevView = current?.state.view;
    current = { state, layout };
    world.innerHTML = '';
    for (const c of layout.cards) world.appendChild(renderCard(state.graph, c, state));
    if (prevView !== state.view) setCamera({ ...DEFAULT_CAM[state.view] }, true);
    computeFit();
    world.classList.remove('enter');
    if (!state.reducedMotion) {
      void world.offsetWidth;
      world.classList.add('enter');
    }
    apply();
    updateHighlights(state);
  }

  function renderCard(g, c, state) {
    const el = h('div', 'card');
    el.dataset.key = c.key;
    el.dataset.type = c.type;
    el.dataset.id = c.id;
    el.style.width = `${c.w}px`;
    el.style.height = `${c.h}px`;
    el.style.transform = `translate3d(${c.x - c.w / 2}px, ${c.y - c.h / 2}px, ${c.z}px) rotateY(${c.rotY || 0}deg)`;
    const face = h('div', 'face');
    face.setAttribute('role', 'button');
    face.tabIndex = 0;
    face.dataset.id = c.id;
    el.appendChild(face);
    const n = G.node(g, c.id);
    face.setAttribute('aria-label', `${G.TYPE_LABEL[n?.type] || ''} ${n?.name || ''}`);
    switch (c.kind) {
      case 'screen': fillScreen(g, face, c, n, state); break;
      case 'feature': fillFeature(g, face, c, n, state); break;
      case 'vm': fillVm(g, face, c, n); break;
      case 'chip': fillChip(g, face, c, n); break;
      case 'state': fillState(g, face, c, n); break;
      case 'detail': fillDetail(g, face, c, n, state); break;
      default: break;
    }
    addAnchors(el, c.key);
    return el;
  }

  function head(face, glyph, name, type, tags = []) {
    const hd = h('div', 'card-head');
    hd.appendChild(h('span', 'glyph', glyph));
    hd.appendChild(h('span', 'name', name));
    for (const t of tags) if (t) hd.appendChild(h('span', `tag ${t}`, t));
    hd.appendChild(h('span', 'type', type));
    face.appendChild(hd);
    return hd;
  }

  function fillScreen(g, face, c, n) {
    head(face, G.TYPE_GLYPH.screen, n.name, 'Screen', [c.isEntry ? 'entry' : '']);
    const body = h('div', 'card-body');
    body.style.padding = '8px';
    body.appendChild(renderPreview(g, {
      screenId: c.id, size: c.size, interactive: c.interactive, highlightId: c.highlightId, highlightIds: c.highlightIds,
      recede: c.recede, anchorPrefix: c.key,
    }));
    face.appendChild(body);
    if (c.caption) face.appendChild(h('div', 'card-foot', c.caption));
  }

  function fillFeature(g, face, c, n) {
    head(face, G.TYPE_GLYPH.feature, n.name, 'Feature');
    const grid = h('div', 'plate-screens');
    for (const s of c.screens) {
      const b = h('button', 'plate-screen');
      b.type = 'button';
      b.dataset.id = s.screen.id;
      b.setAttribute('aria-label', `Screen ${s.screen.name}`);
      const name = h('div', 'name');
      name.appendChild(h('span', null, s.screen.name));
      if (s.isEntry) name.appendChild(h('span', 'tag entry', 'entry'));
      if (c.showStateCounts) name.appendChild(h('span', 'tag', `${s.stateCount} states`));
      b.appendChild(name);
      b.appendChild(renderPreview(g, { screenId: s.screen.id, size: 'xs' }));
      addAnchors(b, `${c.key}/${s.screen.id}`);
      grid.appendChild(b);
    }
    face.appendChild(grid);
    const foot = h('div', 'plate-foot');
    const vm = [...new Set(c.screens.map((s) => G.vmOfScreen(g, s.screen.id)?.name))].filter(Boolean);
    foot.appendChild(h('span', 'sub', `${c.screens.length} screen${c.screens.length === 1 ? '' : 's'} · ${vm.length === 1 ? vm[0] : vm.length + ' view models'}`));
    face.appendChild(foot);
  }

  function fillVm(g, face, c, n) {
    head(face, G.TYPE_GLYPH.viewmodel, n.name, 'View model', [c.tag]);
    const list = h('div', 'members');
    if (c.rows) { list.classList.add('aligned'); list.style.height = `${c.h - 30 - 8}px`; }
    if (c.sub) { const sub = h('div', 'sub vm-sub', c.sub); list.appendChild(sub); }
    for (const m of c.members) {
      const row = h('button', 'member');
      const slot = c.rows?.find((r) => r.id === m.id);
      if (slot) row.style.top = `${slot.y}px`;
      row.type = 'button';
      row.dataset.id = m.id;
      row.setAttribute('aria-label', `${G.TYPE_LABEL[m.type]} ${m.name}`);
      if (c.hot.has(m.id)) row.classList.add('hot');
      if (c.faded.has(m.id)) row.classList.add('faded');
      row.appendChild(h('span', 'glyph', G.TYPE_GLYPH[m.type]));
      row.appendChild(h('span', 'mname', m.name));
      row.appendChild(h('span', 'mtype', m.properties?.clrType || ''));
      addAnchors(row, `${c.key}/${m.id}`);
      list.appendChild(row);
    }
    if (c.more) list.appendChild(h('div', 'member sub', `+ ${c.more} more`));
    face.appendChild(list);
  }

  function fillChip(g, face, c, n) {
    face.parentElement.classList.add('chip');
    const hd = head(face, G.TYPE_GLYPH[n.type], c.title || n.name, '', [c.tag]);
    if (c.mono) hd.querySelector('.name').classList.add('mono');
    if (c.routeId) {
      const rb = h('button', 'tag', 'route');
      rb.type = 'button';
      rb.dataset.id = c.routeId;
      rb.title = 'Inspect the route';
      rb.style.marginLeft = 'auto';
      rb.style.cursor = 'pointer';
      rb.style.background = 'none';
      hd.querySelector('.type').replaceWith(rb);
    }
    if (c.sub) face.appendChild(h('div', 'card-body sub', c.sub));
  }

  function fillState(g, face, c, n) {
    head(face, G.TYPE_GLYPH.state, n.name, c.ownerName || 'State');
    const body = h('div', 'card-body');
    body.style.padding = '8px';
    if (c.screenId) body.appendChild(renderPreview(g, { screenId: c.screenId, size: c.size, stateId: c.id, highlightId: c.highlightId, quiet: c.size !== 'lg' && c.size !== 'md' }));
    face.appendChild(body);
  }

  function fillDetail(g, face, c, n, state) {
    face.parentElement.classList.add('detail');
    head(face, G.TYPE_GLYPH[n.type], n.name, G.TYPE_LABEL[n.type]);
    const body = h('div', 'card-body');
    const kv = h('dl', 'kv');
    const add = (k, v) => { if (v == null || v === '') return; kv.appendChild(h('dt', null, k)); kv.appendChild(h('dd', null, String(v))); };
    const p = n.properties || {};
    if (n.type === 'property' || n.type === 'command') {
      add('type', p.clrType);
      add('expression', p.expression);
      add('default', p.default);
      add('parameter', p.parameter);
      if (p.writable) add('writable', 'yes (TwoWay capable)');
      body.appendChild(kv);
      const rt = h('div', 'runtime-line');
      rt.appendChild(h('span', 'dot'));
      rt.appendChild(h('span', null, state.runtime.connected ? 'live value' : 'live value unavailable · no running app connected'));
      rt.style.marginTop = '8px';
      body.appendChild(rt);
    } else if (n.type === 'route') {
      add('request', p.request);
      add('mechanism', p.mechanism);
      add('qualifier', p.qualifier);
      add('data', p.data);
      body.appendChild(kv);
    } else if (n.type === 'component') {
      add('class', p.uno?.class);
      add('parts', (p.parts || []).join(', '));
      add('props', (p.dependencyProperties || []).join(', '));
      add('uses', G.instancesOf(g, n.id).length);
      body.appendChild(kv);
    }
    face.appendChild(body);
  }

  // ---------- links ----------
  function drawLinks() {
    if (!current) return;
    const svgRect = svg.getBoundingClientRect();
    const anchors = new Map();
    for (const a of world.querySelectorAll('[data-anchor]')) anchors.set(a.dataset.anchor, a);
    const hover = current.state.hoverId;
    const frag = document.createDocumentFragment();
    const defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs');
    frag.appendChild(defs);
    for (const l of current.layout.links) {
      const a = anchors.get(l.from), b = anchors.get(l.to);
      if (!a || !b) continue;
      const ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
      const x1 = ra.left - svgRect.left, y1 = ra.top - svgRect.top;
      const x2 = rb.left - svgRect.left, y2 = rb.top - svgRect.top;
      const horizontal = Math.abs(x2 - x1) >= Math.abs(y2 - y1) * 0.8;
      const d = horizontal
        ? `M${x1},${y1} C${(x1 + x2) / 2},${y1} ${(x1 + x2) / 2},${y2} ${x2},${y2}`
        : `M${x1},${y1} C${x1},${(y1 + y2) / 2} ${x2},${(y1 + y2) / 2} ${x2},${y2}`;
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      path.setAttribute('d', d);
      const idA = l.from.split(':')[0].split('/').pop().split('#')[0];
      const idB = l.to.split(':')[0].split('/').pop().split('#')[0];
      const hot = hover && (idA === hover || idB === hover || l.id === hover);
      path.setAttribute('class', `${l.relation}${l.inferred ? ' inferred' : ''}${hot ? ' hot' : hover ? ' dim' : ''}`);
      frag.appendChild(path);
      if (l.arrow) {
        const ang = horizontal ? (x2 >= x1 ? 0 : Math.PI) : (y2 >= y1 ? Math.PI / 2 : -Math.PI / 2);
        const tri = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
        const s = 5;
        const pts = [[0, 0], [-s * 1.8, -s * 0.9], [-s * 1.8, s * 0.9]].map(([px, py]) => {
          const rx = px * Math.cos(ang) - py * Math.sin(ang), ry = px * Math.sin(ang) + py * Math.cos(ang);
          return `${x2 + rx},${y2 + ry}`;
        });
        tri.setAttribute('points', pts.join(' '));
        tri.setAttribute('class', `arrow ${arrowClass(l.relation)}${hover && !hot ? ' dim' : ''}`);
        if (hover && !hot) tri.style.opacity = '.25';
        frag.appendChild(tri);
      }
      if (l.label) {
        const t = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        const tt = l.labelT ?? 0.5;
        const cp = horizontal ? [[(x1 + x2) / 2, y1], [(x1 + x2) / 2, y2]] : [[x1, (y1 + y2) / 2], [x2, (y1 + y2) / 2]];
        const bz = (a, b, c2, d) => (1 - tt) ** 3 * a + 3 * (1 - tt) ** 2 * tt * b + 3 * (1 - tt) * tt ** 2 * c2 + tt ** 3 * d;
        t.setAttribute('x', bz(x1, cp[0][0], cp[1][0], x2));
        t.setAttribute('y', bz(y1, cp[0][1], cp[1][1], y2) - 5);
        t.setAttribute('text-anchor', 'middle');
        t.setAttribute('class', `label${l.inferred ? ' inferred' : ''}`);
        if (hover && !hot) t.style.opacity = '.3';
        t.textContent = l.inferred ? `${l.label} · inferred` : l.label;
        frag.appendChild(t);
      }
    }
    svg.replaceChildren(frag);
  }
  const arrowClass = (rel) => ({ route: 'route', 'navigates-to': 'route', 'binds-to': 'vm', invokes: 'vm', 'depends-on': 'vm', exposes: 'vm', 'uses-viewmodel': 'vm', 'has-state': 'state', 'transitions-to': 'state', 'instance-of': 'comp', contains: 'comp' }[rel] || '');

  function updateHighlights(state) {
    for (const el of world.querySelectorAll('.card')) {
      el.classList.toggle('focus', el.dataset.id === state.focusId && !el.dataset.key.includes('#'));
      el.classList.toggle('hover', !!state.hoverId && el.dataset.id === state.hoverId && el.dataset.id !== state.focusId);
      el.classList.toggle('cursor', !!state.cursorId && el.dataset.id === state.cursorId && state.cursorId !== state.focusId);
    }
    for (const el of world.querySelectorAll('.region, .member, .plate-screen')) {
      el.classList.toggle('hover', !!state.hoverId && el.dataset.id === state.hoverId);
      if (el.classList.contains('member')) el.classList.toggle('focus', el.dataset.id === state.focusId);
    }
    drawLinks();
  }

  // ---------- input ----------
  let drag = null;
  stage.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    drag = { x: e.clientX, y: e.clientY, yaw: target.yaw, pitch: target.pitch, moved: false, pointerId: e.pointerId };
  });
  stage.addEventListener('pointermove', (e) => {
    if (!drag) return;
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    if (!drag.moved && Math.hypot(dx, dy) < 5) return;
    if (!drag.moved) { drag.moved = true; stage.classList.add('dragging'); try { stage.setPointerCapture(drag.pointerId); } catch { /* pointer gone */ } }
    if (current?.state.view === 'flat') return;
    setCamera({ yaw: drag.yaw + dx * 0.25, pitch: drag.pitch - dy * 0.18 }, true);
  });
  const endDrag = () => {
    if (drag?.moved) { suppressClick = true; setTimeout(() => (suppressClick = false), 0); }
    drag = null;
    stage.classList.remove('dragging');
  };
  stage.addEventListener('pointerup', endDrag);
  stage.addEventListener('pointercancel', endDrag);

  world.addEventListener('click', (e) => {
    if (suppressClick) return;
    const hit = e.target.closest('[data-id]');
    if (!hit) return;
    e.stopPropagation();
    handlers.onFocus(hit.dataset.id);
  });
  world.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' && e.key !== ' ') return;
    const hit = e.target.closest('[data-id]');
    if (!hit) return;
    e.preventDefault();
    handlers.onFocus(hit.dataset.id);
  });
  world.addEventListener('pointerover', (e) => {
    const hit = e.target.closest('[data-id]');
    handlers.onHover(hit ? hit.dataset.id : null);
  });
  world.addEventListener('pointerleave', () => handlers.onHover(null));
  world.addEventListener('focusin', (e) => {
    const hit = e.target.closest('[data-id]');
    if (hit) handlers.onCursor(hit.dataset.id);
  });

  // semantic zoom: continuous scale until a threshold, then a level change
  let zoomLock = 0;
  viewer.addEventListener('wheel', (e) => {
    e.preventDefault();
    if (performance.now() < zoomLock) return;
    const factor = Math.exp(-e.deltaY * 0.0016);
    const next = clamp(target.scale * factor, 0.45, 2.4);
    const hit = e.target.closest?.('[data-id]');
    const over = hit?.dataset.id || current?.state.hoverId || null;
    if (next >= 1.6 && over && over !== current?.state.focusId) {
      zoomLock = performance.now() + 450;
      setCamera({ scale: 1 }, true);
      handlers.onZoomIn(over);
      return;
    }
    if (next <= 0.62) {
      zoomLock = performance.now() + 450;
      setCamera({ scale: 1 }, true);
      handlers.onZoomOut();
      return;
    }
    setCamera({ scale: next }, true);
  }, { passive: false });

  new ResizeObserver(() => { if (current) { computeFit(); apply(); } }).observe(stage);

  return { render, setCamera, getCamera, resetCamera, updateHighlights, drawLinks, nudge: (dy, dp) => setCamera({ yaw: target.yaw + dy, pitch: target.pitch + dp }) };
}

export { FRAME };

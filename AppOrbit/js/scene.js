// DOM scene: cards on CSS 3D planes, a camera with clamps, and screen-space SVG connectors
// drawn between anchor elements. Links read anchor rects back from layout each frame.

import * as G from './graph.js';
import { renderPreview, addAnchors } from './preview.js';
import { glyph } from './icons.js';
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
  const cardEls = new Map(); // key → { el, card }
  let offsets = loadOffsets();
  const layoutId = (state, layout) => `${layout.level}:${state.focusId || ''}:${state.lens}:${state.view}:${state.mode}`;
  const offsetOf = (key) => (current && offsets[layoutId(current.state, current.layout)]?.[key]) || { x: 0, y: 0 };
  function placeCard(el, c) {
    const o = offsetOf(c.key);
    el.style.transform = `translate3d(${c.x + o.x - c.w / 2}px, ${c.y + o.y - c.h / 2}px, ${c.z}px) rotateY(${c.rotY || 0}deg)`;
  }
  function resetOffsets() {
    if (!current) return;
    delete offsets[layoutId(current.state, current.layout)];
    saveOffsets(offsets);
    for (const { el, card } of cardEls.values()) placeCard(el, card);
    drawLinks();
    handlers.onLayoutChange?.(false);
  }

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
    cardEls.clear();
    for (const c of layout.cards) {
      const el = renderCard(state.graph, c, state);
      cardEls.set(c.key, { el, card: c });
      world.appendChild(el);
    }
    handlers.onLayoutChange?.(!!offsets[layoutId(state, layout)]);
    buildLinks();
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
    placeCard(el, c);
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

  function head(face, type0, name, type, tags = []) {
    const hd = h('div', 'card-head');
    hd.appendChild(glyph(type0));
    hd.appendChild(h('span', 'name', name));
    for (const t of tags) if (t) hd.appendChild(h('span', `tag ${t}`, t));
    hd.appendChild(h('span', 'type', type));
    face.appendChild(hd);
    return hd;
  }

  function fillScreen(g, face, c, n) {
    head(face, 'screen', n.name, 'Screen', [c.isEntry ? 'entry' : '']);
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
    head(face, 'feature', n.name, 'Feature');
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
    head(face, 'viewmodel', n.name, 'View model', [c.tag]);
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
      row.appendChild(glyph(m.type));
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
    const hd = head(face, n.type, c.title || n.name, '', [c.tag]);
    if (c.mono) hd.querySelector('.name').classList.add('mono');
    if (c.routeId) {
      const rb = h('button', 'route-btn');
      rb.type = 'button';
      rb.dataset.id = c.routeId;
      rb.title = 'Inspect the route';
      rb.setAttribute('aria-label', 'Inspect the route');
      rb.appendChild(glyph('route'));
      hd.querySelector('.type').replaceWith(rb);
    }
    if (c.sub) face.appendChild(h('div', 'card-body sub', c.sub));
  }

  function fillState(g, face, c, n) {
    head(face, 'state', n.name, c.ownerName || 'State');
    const body = h('div', 'card-body');
    body.style.padding = '8px';
    if (c.screenId) body.appendChild(renderPreview(g, { screenId: c.screenId, size: c.size, stateId: c.id, highlightId: c.highlightId, quiet: c.size !== 'lg' && c.size !== 'md' }));
    face.appendChild(body);
  }

  function fillDetail(g, face, c, n, state) {
    face.parentElement.classList.add('detail');
    head(face, n.type, n.name, G.TYPE_LABEL[n.type]);
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
  // Links are resolved once per render (anchor elements + SVG nodes); each frame only reads the
  // rects the links need and updates attributes in place.
  let linkNodes = [];
  const NS = 'http://www.w3.org/2000/svg';
  const linkEnds = (l) => [l.from.split(':')[0].split('/').pop().split('#')[0], l.to.split(':')[0].split('/').pop().split('#')[0]];
  function buildLinks() {
    const anchors = new Map();
    for (const a of world.querySelectorAll('[data-anchor]')) anchors.set(a.dataset.anchor, a);
    const frag = document.createDocumentFragment();
    linkNodes = [];
    for (const l of current.layout.links) {
      const a = anchors.get(l.from), b = anchors.get(l.to);
      if (!a || !b) continue;
      const path = document.createElementNS(NS, 'path');
      path.setAttribute('class', `${l.relation}${l.inferred ? ' inferred' : ''}`);
      frag.appendChild(path);
      let tri = null, text = null;
      if (l.arrow) {
        tri = document.createElementNS(NS, 'polygon');
        tri.setAttribute('class', `arrow ${arrowClass(l.relation)}`);
        frag.appendChild(tri);
      }
      if (l.label) {
        text = document.createElementNS(NS, 'text');
        text.setAttribute('text-anchor', 'middle');
        text.setAttribute('class', `label${l.inferred ? ' inferred' : ''}`);
        text.textContent = l.inferred ? `${l.label} · inferred` : l.label;
        frag.appendChild(text);
      }
      const [idA, idB] = linkEnds(l);
      linkNodes.push({ l, a, b, path, tri, text, idA, idB });
    }
    svg.replaceChildren(frag);
  }
  function drawLinks() {
    if (!current) return;
    const svgRect = svg.getBoundingClientRect();
    const hover = current.state.hoverId;
    for (const n of linkNodes) {
      const { l, path, tri, text } = n;
      const ra = n.a.getBoundingClientRect(), rb = n.b.getBoundingClientRect();
      const x1 = ra.left - svgRect.left, y1 = ra.top - svgRect.top;
      const x2 = rb.left - svgRect.left, y2 = rb.top - svgRect.top;
      const horizontal = Math.abs(x2 - x1) >= Math.abs(y2 - y1) * 0.8;
      path.setAttribute('d', horizontal
        ? `M${x1},${y1} C${(x1 + x2) / 2},${y1} ${(x1 + x2) / 2},${y2} ${x2},${y2}`
        : `M${x1},${y1} C${x1},${(y1 + y2) / 2} ${x2},${(y1 + y2) / 2} ${x2},${y2}`);
      const hot = !!hover && (n.idA === hover || n.idB === hover || l.id === hover);
      const dim = !!hover && !hot;
      path.classList.toggle('hot', hot);
      path.classList.toggle('dim', dim);
      if (tri) {
        const ang = horizontal ? (x2 >= x1 ? 0 : Math.PI) : (y2 >= y1 ? Math.PI / 2 : -Math.PI / 2);
        const c = Math.cos(ang), sn = Math.sin(ang), s = 5;
        const pts = [[0, 0], [-s * 1.8, -s * 0.9], [-s * 1.8, s * 0.9]].map(([px, py]) => `${x2 + px * c - py * sn},${y2 + px * sn + py * c}`);
        tri.setAttribute('points', pts.join(' '));
        tri.style.opacity = dim ? '.25' : '';
      }
      if (text) {
        const tt = l.labelT ?? 0.5;
        const cp = horizontal ? [[(x1 + x2) / 2, y1], [(x1 + x2) / 2, y2]] : [[x1, (y1 + y2) / 2], [x2, (y1 + y2) / 2]];
        const bz = (p0, p1, p2, p3) => (1 - tt) ** 3 * p0 + 3 * (1 - tt) ** 2 * tt * p1 + 3 * (1 - tt) * tt ** 2 * p2 + tt ** 3 * p3;
        text.setAttribute('x', bz(x1, cp[0][0], cp[1][0], x2));
        text.setAttribute('y', bz(y1, cp[0][1], cp[1][1], y2) + (l.labelDy ?? -5));
        text.style.opacity = dim ? '.3' : '';
      }
    }
  }
  const arrowClass = (rel) => ({ route: 'route', 'navigates-to': 'route', 'binds-to': 'vm', invokes: 'vm', 'depends-on': 'vm', exposes: 'vm', 'uses-viewmodel': 'vm', 'has-state': 'state', 'transitions-to': 'state', 'instance-of': 'comp', contains: 'comp' }[rel] || '');

  function updateHighlights(state) {
    const { focusId, hoverId, cursorId } = state;
    for (const { el } of cardEls.values()) {
      const id = el.dataset.id;
      el.classList.toggle('focus', id === focusId && !el.dataset.key.includes('#'));
      el.classList.toggle('hover', !!hoverId && id === hoverId && id !== focusId);
      el.classList.toggle('cursor', !!cursorId && id === cursorId && cursorId !== focusId);
    }
    for (const el of world.querySelectorAll('.region.hover, .member.hover, .plate-screen.hover')) el.classList.remove('hover');
    if (hoverId) for (const el of world.querySelectorAll(`.region[data-id="${CSS.escape(hoverId)}"], .member[data-id="${CSS.escape(hoverId)}"], .plate-screen[data-id="${CSS.escape(hoverId)}"]`)) el.classList.add('hover');
    for (const el of world.querySelectorAll('.member.focus')) el.classList.remove('focus');
    if (focusId) for (const el of world.querySelectorAll(`.member[data-id="${CSS.escape(focusId)}"]`)) el.classList.add('focus');
    drawLinks();
  }

  /** Move a card by a scene-unit delta (keyboard nudge). */
  function nudgeCard(id, dx, dy) {
    if (!current) return false;
    const entry = [...cardEls.values()].find((e) => e.el.dataset.id === id && !e.card.key.includes('#')) || [...cardEls.values()].find((e) => e.el.dataset.id === id);
    if (!entry) return false;
    const lid = layoutId(current.state, current.layout);
    offsets[lid] = offsets[lid] || {};
    const o = offsets[lid][entry.card.key] || { x: 0, y: 0 };
    offsets[lid][entry.card.key] = { x: o.x + dx, y: o.y + dy };
    placeCard(entry.el, entry.card);
    drawLinks();
    saveOffsets(offsets);
    handlers.onLayoutChange?.(true);
    return true;
  }
  function resetCard(key) {
    if (!current) return;
    const lid = layoutId(current.state, current.layout);
    if (!offsets[lid]?.[key]) return;
    delete offsets[lid][key];
    if (Object.keys(offsets[lid]).length === 0) delete offsets[lid];
    const entry = cardEls.get(key);
    if (entry) placeCard(entry.el, entry.card);
    drawLinks();
    saveOffsets(offsets);
    handlers.onLayoutChange?.(!!offsets[lid]);
  }

  // ---------- input ----------
  let drag = null;
  stage.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    const cardEl = e.target.closest('.card');
    const key = cardEl?.dataset.key;
    const base = key ? offsetOf(key) : null;
    drag = { x: e.clientX, y: e.clientY, yaw: target.yaw, pitch: target.pitch, moved: false, pointerId: e.pointerId, key, base };
  });
  stage.addEventListener('pointermove', (e) => {
    if (!drag) return;
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    if (!drag.moved && Math.hypot(dx, dy) < 5) return;
    if (!drag.moved) { drag.moved = true; stage.classList.add(drag.key ? 'moving' : 'dragging'); try { stage.setPointerCapture(drag.pointerId); } catch { /* pointer gone */ } }
    if (drag.key) {
      // move the card in scene units: undo the camera scale and the foreshortening of the orbit
      const S = fit.scale * cam.scale;
      const kx = Math.max(0.5, Math.cos((cam.yaw * Math.PI) / 180));
      const ky = Math.max(0.5, Math.cos((cam.pitch * Math.PI) / 180));
      const id = layoutId(current.state, current.layout);
      offsets[id] = offsets[id] || {};
      offsets[id][drag.key] = { x: Math.round(drag.base.x + dx / (S * kx)), y: Math.round(drag.base.y + dy / (S * ky)) };
      const entry = cardEls.get(drag.key);
      if (entry) { placeCard(entry.el, entry.card); drawLinks(); }
      return;
    }
    if (current?.state.view === 'flat') return;
    setCamera({ yaw: drag.yaw + dx * 0.25, pitch: drag.pitch - dy * 0.18 }, true);
  });
  const endDrag = () => {
    if (drag?.moved) {
      suppressClick = true;
      setTimeout(() => (suppressClick = false), 0);
      if (drag.key) { saveOffsets(offsets); handlers.onLayoutChange?.(true); }
    }
    drag = null;
    stage.classList.remove('dragging', 'moving');
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
  world.addEventListener('dblclick', (e) => {
    const cardEl = e.target.closest('.card');
    if (cardEl) resetCard(cardEl.dataset.key);
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
    // after a level change, swallow the rest of the gesture (trackpad momentum) until the events pause
    if (performance.now() < zoomLock) { zoomLock = performance.now() + 250; return; }
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

  return { render, setCamera, getCamera, resetCamera, resetOffsets, nudgeCard, updateHighlights, drawLinks, nudge: (dy, dp) => setCamera({ yaw: target.yaw + dy, pitch: target.pitch + dp }) };
}

export { FRAME };

function loadOffsets() {
  try { return JSON.parse(localStorage.getItem('app-orbit.offsets') || '{}') || {}; } catch { return {}; }
}
function saveOffsets(o) {
  try { localStorage.setItem('app-orbit.offsets', JSON.stringify(o)); } catch { /* per-session only */ }
}

// Wireframe screen previews from node.preview specs. Regions are selectable component instances.

import * as G from './graph.js';
import { FRAME } from './layout.js';

const h = (tag, cls, text) => {
  const el = document.createElement(tag);
  if (cls) el.className = cls;
  if (text != null) el.textContent = text;
  return el;
};
const pct = (v) => `${(v * 100).toFixed(2)}%`;
const place = (el, r) => {
  el.style.left = pct(r.x); el.style.top = pct(r.y); el.style.width = pct(r.w); el.style.height = pct(r.h);
  return el;
};
const near = (a, b) => Math.abs(a.x - b.x) < 0.015 && Math.abs(a.y - b.y) < 0.015 && Math.abs(a.w - b.w) < 0.015 && Math.abs(a.h - b.h) < 0.015;

function renderPart(part) {
  const el = h('div', `part p-${part.kind}`);
  place(el, part.rect);
  switch (part.kind) {
    case 'heading':
    case 'text':
      if (part.rows) {
        const bars = h('div', 'bars');
        for (let i = 0; i < part.rows; i++) bars.appendChild(h('div', `bar${i === part.rows - 1 ? ' short' : ''}`));
        el.appendChild(bars);
      } else el.textContent = part.label || '';
      break;
    case 'button': el.classList.add(part.emphasis || 'primary'); el.textContent = part.label || ''; break;
    case 'list':
      for (let i = 0; i < (part.rows || 3); i++) {
        const row = h('div', 'row');
        row.appendChild(h('div', 'thumb'));
        const bars = h('div', 'bars');
        bars.appendChild(h('div', 'bar'));
        bars.appendChild(h('div', 'bar short'));
        row.appendChild(bars);
        el.appendChild(row);
      }
      break;
    case 'input': el.textContent = part.label || ''; break;
    case 'stepper': el.append(h('span', null, '−'), h('span', null, part.label || '1'), h('span', null, '+')); break;
    case 'badge': el.textContent = part.label || ''; break;
    case 'empty': el.textContent = part.label || 'Nothing here'; break;
    case 'alert': el.textContent = part.label || 'Something went wrong'; break;
    default: break;
  }
  return el;
}

/**
 * @param g indexed graph
 * @param o { screenId, size, stateId, interactive, highlightId, highlightIds, recede, anchorPrefix, static }
 */
export function renderPreview(g, o) {
  const screen = G.node(g, o.screenId);
  const [w, hgt] = FRAME[o.size || 'lg'];
  const root = h('div', 'preview');
  root.style.width = `${w}px`;
  root.style.height = `${hgt}px`;
  root.style.setProperty('--u', `${(w / 220).toFixed(3)}px`);
  if (!screen?.preview?.parts) return root;

  const instances = G.instancesOfScreen(g, screen.id).map((x) => x.node).filter((n) => n.preview?.bounds);
  const highlight = new Set([...(o.highlightIds || []), ...(o.highlightId ? [o.highlightId] : [])]);
  const state = o.stateId ? G.node(g, o.stateId) : null;

  for (const part of screen.preview.parts) {
    const el = renderPart(part);
    const owner = instances.find((i) => near(i.preview.bounds, part.rect));
    if (owner) el.dataset.inst = owner.id;
    if (owner && highlight.has(owner.id)) el.classList.add('keep');
    root.appendChild(el);
  }
  if (o.recede) root.classList.add('recede');
  if (o.quiet) root.classList.add('quiet');

  if (state?.preview) {
    for (const part of state.preview.overrides || []) root.appendChild(renderPart(part)).classList.add('override');
    for (const id of state.preview.dim || []) {
      const inst = instances.find((i) => i.id === id);
      if (inst) root.appendChild(place(h('div', 'veil'), inst.preview.bounds));
    }
    for (const id of state.preview.hide || []) {
      const inst = instances.find((i) => i.id === id);
      if (inst) root.appendChild(place(h('div', 'veil hide'), inst.preview.bounds));
    }
  }

  if (o.interactive || highlight.size) {
    for (const inst of instances) {
      const region = h(o.interactive ? 'button' : 'div', 'region');
      if (o.interactive) { region.type = 'button'; region.setAttribute('aria-label', `${inst.name}, instance`); }
      else region.classList.add('static');
      place(region, inst.preview.bounds);
      region.dataset.id = inst.id;
      if (highlight.has(inst.id)) region.classList.add('focus');
      else if (o.recede) region.classList.add('faded');
      region.appendChild(h('span', 'rlabel', inst.name));
      if (o.anchorPrefix) addAnchors(region, `${o.anchorPrefix}/${inst.id}`);
      root.appendChild(region);
    }
  }
  return root;
}

export function addAnchors(el, key) {
  const sides = { l: ['0%', '50%'], r: ['100%', '50%'], t: ['50%', '0%'], b: ['50%', '100%'], c: ['50%', '50%'] };
  for (const [s, [x, y]] of Object.entries(sides)) {
    const a = document.createElement('i');
    a.className = 'anchor';
    a.dataset.anchor = `${key}:${s}`;
    a.style.left = x; a.style.top = y;
    el.appendChild(a);
  }
}

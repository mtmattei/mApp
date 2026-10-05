// Details, declared facts, runtime line, relationships and evidence for the focused entity.

import * as G from './graph.js';

const h = (tag, cls, text) => {
  const el = document.createElement(tag);
  if (cls) el.className = cls;
  if (text != null) el.textContent = text;
  return el;
};

const REL_WORDS = {
  'belongs-to': ['belongs to', 'contains screen'], contains: ['contains', 'contained by'], 'instance-of': ['instance of', 'has instance'],
  'uses-viewmodel': ['uses view model', 'used by screen'], exposes: ['exposes', 'exposed by'], 'binds-to': ['binds to', 'bound by'],
  invokes: ['invokes', 'invoked by'], 'navigates-to': ['navigates to', 'reached by route'], 'entered-via': ['starts route', 'started by'],
  'has-state': ['has state', 'state of'], 'transitions-to': ['transitions to', 'transitions from'], 'depends-on': ['depends on', 'depended on by'],
};

export function renderInspector(root, state, actions) {
  const g = state.graph;
  root.innerHTML = '';
  if (!state.focusId) return renderApplication(root, g, state, actions);
  const n = G.node(g, state.focusId);
  if (!n) return;

  const eyebrow = h('div', 'eyebrow');
  eyebrow.appendChild(h('span', `glyph ${n.type}`, G.TYPE_GLYPH[n.type]));
  eyebrow.appendChild(h('span', null, G.TYPE_LABEL[n.type]));
  const chain = G.contextChain(g, n.id, state.trail);
  const ctx = chain.slice(0, -1).map((c) => c.name).join(' › ');
  if (ctx) eyebrow.appendChild(h('span', null, `· ${ctx}`));
  root.appendChild(eyebrow);
  root.appendChild(h('h2', null, n.name));
  if (n.summary) root.appendChild(h('p', 'summary', n.summary));

  const acts = h('div', 'actions');
  if (n.source) acts.appendChild(button('Open source', () => actions.openSource(n), 'primary'));
  if (n.type === 'component') acts.appendChild(button(`Find all uses (${G.instancesOf(g, n.id).length})`, () => actions.focus(n.id, 'structure')));
  if (n.type === 'component-instance' && G.definitionOf(g, n.id)) acts.appendChild(button('Other uses', () => actions.focusLens(G.definitionOf(g, n.id).id)));
  if (n.type === 'viewmodel') acts.appendChild(button(`Screens served (${G.screensUsingVm(g, n.id).length})`, () => actions.focusLens(n.id)));
  const parent = G.parentOf(g, n.id, state.trail);
  if (parent) acts.appendChild(button(`Zoom out to ${parent.name}`, () => actions.focus(parent.id)));
  root.appendChild(acts);

  // declared facts
  const facts = flatten(n.properties || {});
  if (facts.length) {
    root.appendChild(section('Declared'));
    const kv = h('dl', 'kv');
    for (const [k, v] of facts) { kv.appendChild(h('dt', null, k)); kv.appendChild(h('dd', null, v)); }
    root.appendChild(kv);
  }

  // runtime
  root.appendChild(section('Runtime'));
  const rt = h('div', 'runtime');
  if (state.runtime.connected) rt.textContent = 'Connected.';
  else {
    rt.appendChild(h('div', null, 'Live values unavailable. No running app is connected.'));
    if (n.type === 'property' && n.properties?.default != null) rt.appendChild(h('div', 'mono', `declared default: ${n.properties.default}`));
    if (n.type === 'state' && n.properties?.trigger) rt.appendChild(h('div', 'mono', `active when ${n.properties.trigger}`));
    if (n.type === 'property' && n.properties?.expression) rt.appendChild(h('div', 'mono', `computed: ${n.properties.expression}`));
  }
  root.appendChild(rt);

  // relationships
  const outs = G.outEdges(g, n.id), ins = G.inEdges(g, n.id);
  root.appendChild(section('Relationships', outs.length + ins.length));
  const groups = new Map();
  for (const e of outs) push(groups, `${e.relation}|out`, { e, other: G.node(g, e.to) });
  for (const e of ins) push(groups, `${e.relation}|in`, { e, other: G.node(g, e.from) });
  for (const [key, items] of groups) {
    const [rel, dir] = key.split('|');
    const grp = h('div', 'rel-group');
    grp.appendChild(h('div', 'rel-name', REL_WORDS[rel]?.[dir === 'out' ? 0 : 1] || rel));
    for (const { e, other } of items) {
      if (!other) continue;
      const b = h('button', 'rel');
      b.type = 'button';
      b.appendChild(h('span', `glyph ${other.type}`, G.TYPE_GLYPH[other.type]));
      const name = h('span', null, other.name);
      if (e.label) name.appendChild(h('span', 'rlabel', `  ${e.label}`));
      if (G.isInferred(e)) name.appendChild(h('span', 'tag inferred', ` inferred ${Math.round((e.evidence.confidence || 0) * 100)}%`));
      b.appendChild(name);
      const screen = other.type === 'component-instance' ? G.hostScreenOf(g, other.id) : null;
      b.appendChild(h('span', 'ctx', screen ? screen.name : G.TYPE_LABEL[other.type]));
      b.addEventListener('click', () => actions.focus(other.id));
      b.addEventListener('pointerenter', () => actions.hover(other.id));
      b.addEventListener('pointerleave', () => actions.hover(null));
      grp.appendChild(b);
    }
    root.appendChild(grp);
  }

  // evidence
  root.appendChild(section('Evidence'));
  root.appendChild(evidence(g, n.evidence, n.source, actions));
  const inferredEdges = [...outs, ...ins].filter(G.isInferred);
  for (const e of inferredEdges) {
    const box = evidence(g, e.evidence, e.evidence.source, actions);
    box.prepend(h('div', 'sub', `${G.node(g, e.from).name} → ${e.relation} → ${G.node(g, e.to).name}`));
    root.appendChild(box);
  }
}

function renderApplication(root, g, state, actions) {
  const r = g.raw;
  root.appendChild(h('div', 'eyebrow', 'Application'));
  root.appendChild(h('h2', null, r.name));
  if (r.description) root.appendChild(h('p', 'summary', r.description));
  const acts = h('div', 'actions');
  const entry = G.node(g, r.entry);
  if (entry) acts.appendChild(button(`Go to entry: ${entry.name}`, () => actions.focus(entry.id), 'primary'));
  root.appendChild(acts);
  root.appendChild(section('Graph'));
  const stats = h('div', 'stat-row');
  for (const [type, label] of Object.entries(G.TYPE_LABEL)) {
    const c = G.nodesOf(g, type).length;
    if (c) { const s = h('span'); s.appendChild(h('b', null, String(c))); s.append(` ${label.toLowerCase()}${c === 1 ? '' : 's'}`); stats.appendChild(s); }
  }
  const e = h('span'); e.appendChild(h('b', null, String(r.edges.length))); e.append(' relationships');
  stats.appendChild(e);
  root.appendChild(stats);
  const inferred = r.edges.filter(G.isInferred).length;
  root.appendChild(h('p', 'summary', `${inferred} relationship${inferred === 1 ? ' is' : 's are'} inferred and marked as such. Everything else is declared in source.`));
  root.appendChild(section('Features'));
  for (const f of G.nodesOf(g, 'feature')) {
    const b = h('button', 'rel');
    b.type = 'button';
    b.appendChild(h('span', 'glyph feature', G.TYPE_GLYPH.feature));
    b.appendChild(h('span', null, f.name));
    b.appendChild(h('span', 'ctx', `${G.screensOfFeature(g, f.id).length} screens`));
    b.addEventListener('click', () => actions.focus(f.id));
    root.appendChild(b);
  }
  if (r.unresolved?.length) {
    root.appendChild(section('Unresolved'));
    const ul = h('ul', 'unresolved');
    for (const u of r.unresolved) ul.appendChild(h('li', null, u));
    root.appendChild(ul);
  }
}

function evidence(g, ev, source, actions) {
  const box = h('div', `evidence ${ev?.kind || ''}`);
  if (!ev) { box.textContent = 'No evidence recorded.'; return box; }
  const head = h('div', 'ev-head');
  head.appendChild(h('span', `tag ${ev.kind}`, ev.kind));
  head.appendChild(h('span', null, `confidence ${Math.round((ev.confidence ?? 0) * 100)}%`));
  box.appendChild(head);
  if (ev.rationale) box.appendChild(h('div', 'rationale', ev.rationale));
  const ref = ev.source || source;
  const fl = G.fileLine(g, ref);
  if (fl) {
    const src = h('button', 'link-button src', `${fl.path}:${ref.line}`);
    src.type = 'button';
    src.addEventListener('click', () => actions.openSourceRef(ref));
    box.appendChild(src);
    box.appendChild(h('pre', null, fl.text.trim()));
  }
  return box;
}

function flatten(obj, prefix = '') {
  const out = [];
  for (const [k, v] of Object.entries(obj)) {
    if (v && typeof v === 'object' && !Array.isArray(v)) out.push(...flatten(v, `${prefix}${k}.`));
    else out.push([`${prefix}${k}`, Array.isArray(v) ? v.join(', ') : String(v)]);
  }
  return out;
}
const push = (map, k, v) => { if (!map.has(k)) map.set(k, []); map.get(k).push(v); };
function section(title, count) {
  const el = h('h3', null, title);
  if (count != null) el.appendChild(h('span', 'count', String(count)));
  return el;
}
function button(label, onClick, cls = '') {
  const b = h('button', cls, label);
  b.type = 'button';
  b.addEventListener('click', onClick);
  return b;
}

// Details for the focused entity: what it is, its declared facts, what it connects to, and the evidence.
// The breadcrumb owns the location; the editor owns the source view. This panel does not repeat them.

import * as G from './graph.js';
import { glyph } from './icons.js';

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
  'has-state': ['has state', 'state of'], 'transitions-to': ['transitions to', 'transitions from'], 'depends-on': ['depends on', 'read by'],
};

const LABELS = {
  'uno.type': 'control', 'uno.class': 'class', 'uno.xName': 'x:Name', 'uno.styleKey': 'style', 'uno.pattern': 'pattern', 'uno.model': 'model',
  'uno.mechanism': 'mechanism', 'uno.member': 'member', 'uno.property': 'property', 'uno.data': 'data', clrType: 'type', isEntry: 'entry',
  dependencyProperties: 'properties', expression: 'expression', default: 'default', writable: 'writable', parameter: 'parameter', async: 'async',
  route: 'route', request: 'request', mechanism: 'mechanism', qualifier: 'qualifier', data: 'data', trigger: 'when', duration: 'duration', origin: 'origin', parts: 'parts',
};

const PLURAL = {
  feature: ['feature', 'features'], screen: ['screen', 'screens'], component: ['component', 'components'], 'component-instance': ['instance', 'instances'],
  viewmodel: ['view model', 'view models'], property: ['property', 'properties'], command: ['command', 'commands'], state: ['state', 'states'], route: ['route', 'routes'],
};

export function renderInspector(root, state, actions) {
  const g = state.graph;
  root.innerHTML = '';
  if (!state.focusId) return renderApplication(root, g, state, actions);
  const n = G.node(g, state.focusId);
  if (!n) return;

  // ---- identity ----
  const eyebrow = h('div', 'eyebrow');
  eyebrow.appendChild(glyph(n.type));
  eyebrow.appendChild(h('span', null, G.TYPE_LABEL[n.type]));
  root.appendChild(eyebrow);
  root.appendChild(h('h2', null, n.name));
  if (n.summary) root.appendChild(h('p', 'summary', n.summary));
  root.appendChild(sourceRow(g, n.source, state.workspaceRoot, actions));

  // ---- declared ----
  const facts = flatten(n.properties || {});
  const hasValue = n.type === 'property' || n.type === 'command' || n.type === 'state';
  if (facts.length || hasValue) {
    root.appendChild(section('Declared'));
    const kv = h('dl', 'kv');
    for (const [k, v] of facts) { kv.appendChild(h('dt', null, LABELS[k] || k)); kv.appendChild(h('dd', null, v)); }
    if (hasValue) {
      kv.appendChild(h('dt', null, 'live value'));
      kv.appendChild(h('dd', state.runtime.connected ? '' : 'muted', state.runtime.connected ? 'connected' : 'unavailable · no running app connected'));
    }
    root.appendChild(kv);
  }

  // ---- relationships ----
  const outs = G.outEdges(g, n.id), ins = G.inEdges(g, n.id);
  const contextScreen = G.contextScreen(g, n.id, state.trail);
  root.appendChild(section('Relationships', outs.length + ins.length));
  if (outs.length + ins.length === 0) root.appendChild(h('p', 'quiet', 'None declared.'));
  const groups = new Map();
  for (const e of outs) push(groups, `${e.relation}|out`, { e, other: G.node(g, e.to) });
  for (const e of ins) push(groups, `${e.relation}|in`, { e, other: G.node(g, e.from) });
  for (const [key, items] of groups) {
    const [rel, dir] = key.split('|');
    const grp = h('div', 'rel-group');
    const name = h('div', 'rel-name', REL_WORDS[rel]?.[dir === 'out' ? 0 : 1] || rel);
    if (items.length > 1) name.appendChild(h('span', 'count', String(items.length)));
    grp.appendChild(name);
    for (const { e, other } of items) {
      if (!other) continue;
      const b = h('button', 'rel');
      b.type = 'button';
      b.appendChild(glyph(other.type));
      const label = h('span', 'rel-main');
      label.appendChild(h('span', 'rel-title', other.type === 'route' ? other.name : other.name));
      if (e.label) label.appendChild(h('span', 'rlabel', e.label));
      if (G.isInferred(e)) label.appendChild(h('span', 'tag inferred', `inferred ${Math.round((e.evidence.confidence || 0) * 100)}%`));
      b.appendChild(label);
      const ctx = contextOf(g, other, contextScreen);
      b.appendChild(h('span', 'ctx', ctx));
      b.addEventListener('click', () => actions.focus(other.id));
      b.addEventListener('pointerenter', () => actions.hover(other.id));
      b.addEventListener('pointerleave', () => actions.hover(null));
      grp.appendChild(b);
    }
    root.appendChild(grp);
  }

  // ---- evidence ----
  root.appendChild(section('Evidence'));
  root.appendChild(evidence(g, n.evidence, n.source, actions));
  for (const e of [...outs, ...ins].filter(G.isInferred)) {
    const box = evidence(g, e.evidence, e.evidence.source, actions);
    box.prepend(h('div', 'ev-edge', `${G.node(g, e.from).name} → ${e.relation} → ${G.node(g, e.to).name}`));
    root.appendChild(box);
  }
}

/** Context worth showing beside a related entity: only when the row does not already imply it. */
function contextOf(g, other, contextScreen) {
  if (other.type === 'component-instance') {
    const host = G.hostScreenOf(g, other.id);
    return host && host.id !== contextScreen?.id ? host.name : '';
  }
  if (other.type === 'property' || other.type === 'command') {
    const vm = G.vmOfMember(g, other.id);
    return vm && vm.name;
  }
  if (other.type === 'screen') return G.featureOfScreen(g, other.id)?.name || '';
  if (other.type === 'state') {
    const owner = G.ownerOfState(g, other.id);
    return owner && owner.id !== contextScreen?.id ? owner.name : '';
  }
  return '';
}

function sourceRow(g, ref, root, actions) {
  const row = h('div', 'source-row');
  const fl = G.fileLine(g, ref);
  if (!fl) { row.appendChild(h('span', 'quiet', 'No source location')); return row; }
  const link = h('button', 'src-link', `${fl.path}:${ref.line}`);
  link.type = 'button';
  link.title = 'Show in the editor';
  link.addEventListener('click', () => actions.openSourceRef(ref));
  row.appendChild(link);
  if (root) {
    const sep = root.includes('\\') ? '\\' : '/';
    const full = root.replace(/[\\/]+$/, '') + sep + fl.path.split('/').join(sep);
    const a = h('a', 'src-link', 'open in VS Code');
    a.href = `vscode://file/${full}:${ref.line}`;
    a.title = full;
    row.appendChild(a);
  }
  return row;
}

function renderApplication(root, g, state, actions) {
  const r = g.raw;
  const eyebrow = h('div', 'eyebrow');
  eyebrow.appendChild(glyph('app'));
  eyebrow.appendChild(h('span', null, 'Application'));
  root.appendChild(eyebrow);
  root.appendChild(h('h2', null, r.name));
  if (r.description) root.appendChild(h('p', 'summary', r.description));
  const entry = G.node(g, r.entry);
  if (entry) {
    const row = h('div', 'source-row');
    const b = h('button', 'src-link', `Entry: ${entry.name}`);
    b.type = 'button';
    b.addEventListener('click', () => actions.focus(entry.id));
    row.appendChild(b);
    root.appendChild(row);
  }
  root.appendChild(section('Graph'));
  const stats = h('div', 'stat-row');
  for (const [type, [one, many]] of Object.entries(PLURAL)) {
    const c = G.nodesOf(g, type).length;
    if (c) { const s = h('span'); s.appendChild(h('b', null, String(c))); s.append(` ${c === 1 ? one : many}`); stats.appendChild(s); }
  }
  const e = h('span'); e.appendChild(h('b', null, String(r.edges.length))); e.append(' relationships');
  stats.appendChild(e);
  root.appendChild(stats);
  const inferred = r.edges.filter(G.isInferred).length;
  root.appendChild(h('p', 'quiet', `${inferred} relationship${inferred === 1 ? ' is' : 's are'} inferred and marked as such. Everything else is declared in source.`));
  root.appendChild(section('Features', G.nodesOf(g, 'feature').length));
  for (const f of G.nodesOf(g, 'feature')) {
    const b = h('button', 'rel');
    b.type = 'button';
    b.appendChild(glyph('feature'));
    const main = h('span', 'rel-main'); main.appendChild(h('span', 'rel-title', f.name)); b.appendChild(main);
    const n = G.screensOfFeature(g, f.id).length;
    b.appendChild(h('span', 'ctx', `${n} screen${n === 1 ? '' : 's'}`));
    b.addEventListener('click', () => actions.focus(f.id));
    root.appendChild(b);
  }
  if (r.unresolved?.length) {
    root.appendChild(section('Unresolved', r.unresolved.length));
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
  head.appendChild(h('span', null, `${Math.round((ev.confidence ?? 0) * 100)}% confidence`));
  box.appendChild(head);
  if (ev.rationale) box.appendChild(h('div', 'rationale', ev.rationale));
  const ref = ev.source || source;
  const fl = G.fileLine(g, ref);
  if (fl) {
    const src = h('button', 'src-link', `${fl.path}:${ref.line}`);
    src.type = 'button';
    src.title = 'Show in the editor';
    src.addEventListener('click', () => actions.openSourceRef(ref));
    box.appendChild(src);
    const first = ref.line - fl.file.startLine;
    const last = Math.min(first + 3, (ref.endLine ?? ref.line) - fl.file.startLine);
    const lines = fl.file.lines.slice(first, last + 1);
    box.appendChild(h('pre', null, dedent(lines).join('\n')));
  }
  return box;
}

function dedent(lines) {
  const indents = lines.filter((l) => l.trim()).map((l) => l.match(/^\s*/)[0].length);
  const min = indents.length ? Math.min(...indents) : 0;
  return lines.map((l) => l.slice(min));
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

// Runs the prototype's pure layout.js over the graph for every (focus, lens, view, mode) and writes
// the fixture tools/LayoutCheck recomputes in C#. Node ≥ 18.
//   node tools/dump-layouts.mjs            (from AppOrbit/App)
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const proto = join(here, '..', '..');
const { indexGraph } = await import(pathToFileURL(join(proto, 'js', 'graph.js')));
const { layout } = await import(pathToFileURL(join(proto, 'js', 'layout.js')));

const graph = indexGraph(JSON.parse(readFileSync(join(proto, 'graph', 'orderly.graph.json'), 'utf8')));
const LENSES = ['structure', 'navigation', 'behavior', 'states'];
const r3 = (v) => (typeof v === 'number' ? Math.round(v * 1000) / 1000 : v);
const sorted = (set) => [...(set || [])].sort();

function normCard(c) {
  return {
    kind: c.kind, key: c.key, type: c.type, id: c.id ?? null,
    x: r3(c.x), y: r3(c.y), z: r3(c.z), w: r3(c.w), h: r3(c.h), rotY: r3(c.rotY || 0),
    size: c.size ?? null, interactive: !!c.interactive, isEntry: !!c.isEntry, highlightId: c.highlightId ?? null,
    highlightIds: c.highlightIds ?? null, recede: !!c.recede, caption: c.caption ?? null, screenId: c.screenId ?? null,
    ownerName: c.ownerName ?? null, big: !!c.big, title: c.title ?? null, sub: c.sub ?? null, tag: c.tag ?? null,
    mono: !!c.mono, routeId: c.routeId ?? null, members: c.members ? c.members.map((m) => m.id) : null,
    rows: c.rows ? c.rows.map((rw) => ({ id: rw.id, y: r3(rw.y), hot: !!rw.hot })) : null,
    hot: sorted(c.hot), faded: sorted(c.faded), more: c.more || 0, expanded: !!c.expanded,
    screens: c.screens ? c.screens.map((s) => ({ id: s.screen.id, stateCount: s.stateCount, isEntry: !!s.isEntry })) : null,
    showStateCounts: !!c.showStateCounts,
  };
}
function normLink(l) {
  return { from: l.from, to: l.to, relation: l.relation, label: l.label || '', arrow: !!l.arrow, id: l.id ?? null, labelT: l.labelT == null ? null : r3(l.labelT), labelDy: l.labelDy == null ? null : r3(l.labelDy), inferred: !!l.inferred };
}

const cases = [];
const focuses = [null, ...graph.raw.nodes.map((n) => n.id)];
for (const focusId of focuses) {
  for (const lens of LENSES) {
    for (const mode of ['expanded', 'docked']) {
      for (const view of mode === 'expanded' ? ['orbit', 'flat'] : ['orbit']) {
        const state = { graph, focusId, lens, view, mode, trail: [], carrying: false };
        const l = layout(state);
        cases.push({
          focusId, lens, view, mode, level: l.level, note: l.note || '',
          bounds: { x: r3(l.bounds.x), y: r3(l.bounds.y), w: r3(l.bounds.w), h: r3(l.bounds.h) },
          cards: l.cards.map(normCard), links: l.links.map(normLink),
        });
      }
    }
  }
}
mkdirSync(join(here, 'fixtures'), { recursive: true });
writeFileSync(join(here, 'fixtures', 'layouts.json'), JSON.stringify({ graph: graph.raw.graphId, nodes: graph.raw.nodes.length, edges: graph.raw.edges.length, cases }));
console.log(`${cases.length} cases, ${cases.reduce((a, c) => a + c.cards.length, 0)} cards, ${cases.reduce((a, c) => a + c.links.length, 0)} links`);

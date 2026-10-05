// (focus, lens, view, mode) → placed cards and links. Pure; no DOM.
// Coordinates are scene units (CSS px at scale 1), origin at the scene centre, y down, z toward the viewer.

import * as G from './graph.js';

export const FRAME = { lg: [220, 392], md: [150, 267], sm: [100, 178], xs: [62, 110] };
const HEAD = 30;
const PAD = 8;

export function previewCardSize(size) {
  const [w, h] = FRAME[size];
  return { w: w + PAD * 2, h: h + HEAD + PAD * 2 };
}

const VM_W = 280;
const CHIP_W = 170;
const CHIP_H = 52;
const MEMBER_H = 26;
const vmHeight = (count, sub = false) => HEAD + 8 + count * MEMBER_H + (sub ? 18 : 0) + 6;

/** Member rows for a view model card whose bound members align with the UI rows they drive.
 *  frac: memberId → vertical fraction of the preview frame. bodyH: preview frame height. */
function alignedRows(members, hot, frac, bodyH, sub = false) {
  const top = sub ? 18 : 0;
  const hotList = members.filter((m) => hot.has(m.id)).map((m) => ({ m, f: frac.get(m.id) ?? 0.5 })).sort((a, b) => a.f - b.f);
  const rows = [];
  let y = top;
  for (const { m, f } of hotList) {
    y = Math.max(top + f * bodyH - MEMBER_H / 2, y);
    rows.push({ id: m.id, y, hot: true });
    y += MEMBER_H;
  }
  let yy = Math.max(y, top + bodyH * 0.55);
  for (const m of members.filter((mm) => !hot.has(mm.id))) { rows.push({ id: m.id, y: yy, hot: false }); yy += MEMBER_H; }
  return { rows, h: HEAD + 8 + yy + 8 };
}

function card(kind, key, type, x, y, z, w, h, opts = {}) {
  return { kind, key, type, x, y, z, w, h, rotY: 0, ...opts };
}
function link(from, to, relation, label = '', extra = {}) {
  return { from, to, relation, label, ...extra };
}
const spread = (i, n, step) => (i - (n - 1) / 2) * step;

export function layout(state) {
  const { graph: g, focusId, lens, view, mode } = state;
  const flat = view === 'flat';
  const docked = mode === 'docked';
  const level = G.levelOf(g, focusId);
  let result;
  switch (level) {
    case 'application': result = layoutApplication(g, lens, flat, docked); break;
    case 'feature': result = layoutFeature(g, focusId, lens, flat, docked); break;
    case 'screen': result = layoutScreen(g, focusId, lens, flat, docked); break;
    case 'component': result = layoutComponent(g, focusId, lens, flat, docked, state.trail); break;
    default: result = layoutDetail(g, focusId, lens, flat, docked, state.trail);
  }
  result.level = level;
  result.bounds = boundsOf(result.cards);
  return result;
}

function boundsOf(cards) {
  if (cards.length === 0) return { x: 0, y: 0, w: 1, h: 1 };
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (const c of cards) {
    x0 = Math.min(x0, c.x - c.w / 2); x1 = Math.max(x1, c.x + c.w / 2);
    y0 = Math.min(y0, c.y - c.h / 2); y1 = Math.max(y1, c.y + c.h / 2);
  }
  return { x: (x0 + x1) / 2, y: (y0 + y1) / 2, w: x1 - x0, h: y1 - y0 };
}

// ------------------------------------------------------------------ application
function layoutApplication(g, lens, flat, docked) {
  const cards = [], links = [];
  const features = G.nodesOf(g, 'feature');
  const plateW = 250;
  const rowH = FRAME.xs[1] + 44;
  const plateOf = new Map();
  features.forEach((f, i) => {
    const screens = G.screensOfFeature(g, f.id);
    const rows = Math.max(1, Math.ceil(screens.length / 2));
    const h = HEAD + 10 + rows * rowH + 30;
    const key = f.id;
    cards.push(card('feature', key, 'feature', spread(i, features.length, 300), 0, 0, plateW, h, {
      id: f.id,
      screens: screens.map((s) => ({ screen: s, stateCount: G.statesOf(g, s.id).length, isEntry: s.id === g.raw.entry })),
      showStateCounts: lens === 'states',
    }));
    for (const s of screens) plateOf.set(s.id, key);
  });

  if (docked) return { cards, links, note: '' };

  if (lens === 'navigation') {
    for (const r of G.nodesOf(g, 'route')) {
      const target = G.routeTarget(g, r.id);
      for (const o of G.routeOrigins(g, r.id)) {
        const fromKey = plateOf.get(o.screen.id), toKey = plateOf.get(target.id);
        if (!fromKey || !toKey) continue;
        const cross = fromKey !== toKey;
        links.push(link(`${fromKey}/${o.screen.id}:${cross ? 'r' : 'c'}`, `${toKey}/${target.id}:${cross ? 'l' : 'c'}`, 'route',
          cross ? (o.via.type === 'command' ? o.via.name : o.via.name) : '', { arrow: true, id: r.id }));
      }
    }
  }
  if (lens === 'behavior') {
    for (const vm of G.nodesOf(g, 'viewmodel')) {
      const screens = G.screensUsingVm(g, vm.id);
      const plates = [...new Set(screens.map((s) => plateOf.get(s.id)))].filter(Boolean);
      const xs = plates.map((k) => cards.find((c) => c.key === k).x);
      const x = xs.reduce((a, b) => a + b, 0) / xs.length;
      const idx = G.nodesOf(g, 'viewmodel').filter((v) => [...new Set(G.screensUsingVm(g, v.id).map((s) => plateOf.get(s.id)))][0] === plates[0]).indexOf(vm);
      const key = vm.id;
      cards.push(card('chip', key, 'viewmodel', x, 190 + idx * 56, flat ? 0 : -160, CHIP_W + 20, CHIP_H, {
        id: vm.id, title: vm.name, sub: screens.length > 1 ? `serves ${screens.length} screens` : `serves ${screens[0]?.name}`,
        tag: screens.length > 1 ? 'shared' : '',
      }));
      for (const s of screens) links.push(link(`${plateOf.get(s.id)}/${s.id}:b`, `${key}:t`, 'uses-viewmodel', '', { arrow: false }));
    }
  }
  if (lens === 'structure') {
    G.nodesOf(g, 'component').forEach((def, i) => {
      const uses = G.screensUsingDefinition(g, def.id);
      const key = def.id;
      cards.push(card('chip', key, 'component', spread(i, G.nodesOf(g, 'component').length, 220), 200, flat ? 0 : 60, CHIP_W + 20, CHIP_H, {
        id: def.id, title: def.name, sub: `${uses.length} uses · ${uses.map((u) => u.screen.name).join(', ')}`,
      }));
      for (const u of uses) links.push(link(`${plateOf.get(u.screen.id)}/${u.screen.id}:b`, `${key}:t`, 'instance-of', '', { arrow: false }));
    });
  }
  return { cards, links, note: lens === 'states' ? 'Declared state counts per screen' : '' };
}

// ------------------------------------------------------------------ feature
function layoutFeature(g, featureId, lens, flat, docked) {
  const cards = [], links = [];
  const screens = G.screensOfFeature(g, featureId);
  const size = docked ? 'sm' : 'md';
  const sz = previewCardSize(size);
  const step = sz.w + (docked ? 40 : 90);
  const keyOf = new Map();
  screens.forEach((s, i) => {
    const key = s.id;
    keyOf.set(s.id, key);
    cards.push(card('screen', key, 'screen', spread(i, screens.length, step), 0, 0, sz.w, sz.h, {
      id: s.id, size, interactive: false, isEntry: s.id === g.raw.entry,
    }));
  });
  const leftX = spread(0, screens.length, step) - sz.w / 2;
  const rightX = -leftX;

  // routes inside the feature are always shown (navigation is what a flow is about)
  if (lens === 'navigation' || docked) {
    const inChips = [], outChips = [];
    for (const s of screens) {
      for (const { route, origin, target } of G.routesFrom(g, s.id)) {
        if (!target) continue;
        const fromAnchor = origin.instance ? `${s.id}/${origin.instance.id}:r` : `${s.id}:r`;
        if (keyOf.has(target.id)) {
          links.push(link(fromAnchor, `${target.id}:l`, 'route', docked ? '' : origin.via.name, { arrow: true, id: route.id }));
        } else if (!docked) {
          outChips.push({ route, origin, target, fromAnchor });
        }
      }
      if (docked) continue;
      for (const { route, origin } of G.routesTo(g, s.id)) {
        if (!keyOf.has(origin.screen.id)) inChips.push({ route, origin, to: s.id });
      }
    }
    inChips.forEach((c, i) => {
      const key = `${c.route.id}#in`;
      cards.push(card('chip', key, 'screen', leftX - 150, spread(i, inChips.length, CHIP_H + 12), 0, CHIP_W, CHIP_H, {
        id: c.origin.screen.id, title: c.origin.screen.name, sub: `via ${c.origin.via.name}`, routeId: c.route.id,
      }));
      links.push(link(`${key}:r`, `${c.to}:l`, 'route', '', { arrow: true, id: c.route.id }));
    });
    outChips.forEach((c, i) => {
      const key = `${c.route.id}#out`;
      cards.push(card('chip', key, 'screen', rightX + 150, spread(i, outChips.length, CHIP_H + 12), 0, CHIP_W, CHIP_H, {
        id: c.target.id, title: c.target.name, sub: G.featureOfScreen(g, c.target.id)?.name || '', routeId: c.route.id,
      }));
      links.push(link(c.fromAnchor, `${key}:l`, 'route', c.origin.edge.label || '', { arrow: true, id: c.route.id }));
    });
  }
  if (docked) return { cards, links, note: '' };

  if (lens === 'behavior') {
    const vms = [...new Set(screens.map((s) => G.vmOfScreen(g, s.id)?.id))].filter(Boolean).map((id) => G.node(g, id));
    vms.forEach((vm) => {
      const users = G.screensUsingVm(g, vm.id).filter((s) => keyOf.has(s.id));
      const x = users.reduce((a, s) => a + cards.find((c) => c.key === s.id).x, 0) / users.length;
      const members = G.membersOfVm(g, vm.id);
      const h = vmHeight(Math.min(members.length, 6)) + (members.length > 6 ? 26 : 0);
      const key = vm.id;
      cards.push(card('vm', key, 'viewmodel', x + 20, sz.h / 2 + 70 + h / 2, flat ? 0 : -200, VM_W, h, {
        id: vm.id, members: members.slice(0, 6), more: Math.max(0, members.length - 6), hot: new Set(), faded: new Set(),
        tag: G.screensUsingVm(g, vm.id).length > 1 ? 'shared' : '',
      }));
      for (const s of users) links.push(link(`${s.id}:b`, `${key}:t`, 'uses-viewmodel', '', { arrow: false }));
    });
  }
  if (lens === 'structure') {
    const defs = new Map();
    for (const s of screens) {
      for (const { node: inst } of G.instancesOfScreen(g, s.id)) {
        const def = G.definitionOf(g, inst.id);
        if (!def) continue;
        if (!defs.has(def.id)) defs.set(def.id, []);
        defs.get(def.id).push({ screen: s, inst });
      }
    }
    [...defs.entries()].forEach(([defId, uses], i) => {
      const def = G.node(g, defId);
      const all = G.screensUsingDefinition(g, defId);
      const key = defId;
      cards.push(card('chip', key, 'component', spread(i, defs.size, 230), sz.h / 2 + 70, flat ? 0 : 60, CHIP_W + 30, CHIP_H, {
        id: defId, title: def.name, sub: `${all.length} uses · ${all.map((u) => u.screen.name).join(', ')}`,
      }));
      for (const u of uses) links.push(link(`${u.screen.id}/${u.inst.id}:c`, `${key}:t`, 'instance-of', '', { arrow: false }));
    });
    if (defs.size === 0) return { cards, links, note: 'No shared component definitions in this feature' };
  }
  if (lens === 'states') {
    for (const s of screens) {
      const sx = cards.find((c) => c.key === s.id).x;
      const states = G.statesOf(g, s.id).slice(0, 4);
      const tsz = previewCardSize('xs');
      states.forEach((st, j) => {
        cards.push(card('state', `${st.id}#thumb`, 'state', sx + spread(j, states.length, tsz.w + 6), sz.h / 2 + 40 + tsz.h / 2, flat ? 0 : 40, tsz.w, tsz.h, {
          id: st.id, screenId: s.id, size: 'xs',
        }));
      });
    }
  }
  return { cards, links, note: '' };
}

// ------------------------------------------------------------------ screen
function layoutScreen(g, screenId, lens, flat, docked) {
  const cards = [], links = [];
  const size = docked ? 'md' : 'lg';
  const sz = previewCardSize(size);
  const front = screenId;
  cards.push(card('screen', front, 'screen', 0, 0, 0, sz.w, sz.h, { id: screenId, size, interactive: !docked, isEntry: screenId === g.raw.entry }));

  if (lens === 'navigation' || docked) {
    const ins = G.routesTo(g, screenId);
    const outs = G.routesFrom(g, screenId);
    const chipW = docked ? 120 : CHIP_W;
    const dx = sz.w / 2 + (docked ? 90 : 170);
    ins.forEach((r, i) => {
      const key = `${r.route.id}#in`;
      cards.push(card('chip', key, 'screen', -dx, spread(i, ins.length, CHIP_H + 12), 0, chipW, CHIP_H, {
        id: r.origin.screen.id, title: r.origin.screen.name, sub: docked ? '' : `via ${r.origin.via.name}`, routeId: r.route.id,
      }));
      links.push(link(`${key}:r`, `${front}:l`, 'route', '', { arrow: true, id: r.route.id }));
    });
    outs.forEach((r, i) => {
      const key = `${r.route.id}#out`;
      cards.push(card('chip', key, 'screen', dx, spread(i, outs.length, CHIP_H + 12), 0, chipW, CHIP_H, {
        id: r.target.id, title: r.target.name, sub: docked ? '' : (r.route.properties?.qualifier ? `clears back stack` : G.featureOfScreen(g, r.target.id)?.name || ''), routeId: r.route.id,
      }));
      const fromAnchor = r.origin.instance && !docked ? `${front}/${r.origin.instance.id}:r` : `${front}:r`;
      links.push(link(fromAnchor, `${key}:l`, 'route', docked ? '' : (r.origin.edge.label || ''), { arrow: true, id: r.route.id }));
    });
    if (docked) return { cards, links, note: '' };
    if (ins.length + outs.length === 0) return { cards, links, note: 'No declared routes touch this screen' };
  }

  if (lens === 'behavior') {
    const vm = G.vmOfScreen(g, screenId);
    if (!vm) return { cards, links, note: 'No view model declared for this screen' };
    const members = G.membersOfVm(g, vm.id);
    const hot = new Set();
    const instIds = new Set(G.instancesOfScreen(g, screenId).map((x) => x.node.id));
    const bindingEdges = [];
    for (const m of members) {
      for (const e of G.inEdges(g, m.id)) {
        if ((e.relation === 'binds-to' || e.relation === 'invokes') && instIds.has(e.from)) {
          hot.add(m.id);
          bindingEdges.push(e);
        }
      }
    }
    // bound members sit at the height of the UI element they drive
    const frac = new Map();
    for (const e of bindingEdges) {
      const inst = G.node(g, e.from);
      const b = inst?.preview?.bounds;
      if (b && !frac.has(e.to)) frac.set(e.to, b.y + b.h / 2);
    }
    const others = G.screensUsingVm(g, vm.id).filter((s) => s.id !== screenId);
    const { rows, h } = alignedRows(members, hot, frac, FRAME[size][1], others.length > 0);
    const key = vm.id;
    cards.push(card('vm', key, 'viewmodel', sz.w / 2 + 210, -sz.h / 2 + h / 2, flat ? 0 : -200, VM_W, h, {
      id: vm.id, members, rows, hot, faded: new Set(members.filter((m) => !hot.has(m.id)).map((m) => m.id)),
      tag: others.length ? 'shared' : '', sub: others.length ? `also serves ${others.map((s) => s.name).join(', ')}` : '',
    }));
    const perSource = new Map();
    for (const e of bindingEdges) {
      const k = perSource.get(e.from) || 0;
      perSource.set(e.from, k + 1);
      const total = bindingEdges.filter((b) => b.from === e.from).length;
      links.push(link(`${front}/${e.from}:r`, `${key}/${e.to}:l`, e.relation, e.label || '', { arrow: e.relation === 'invokes', inferred: G.isInferred(e), labelT: total === 1 ? 0.45 : 0.2 + 0.6 * (k / (total - 1)) }));
    }
    others.forEach((s, i) => {
      const ck = `${s.id}#shared`;
      cards.push(card('chip', ck, 'screen', sz.w / 2 + 210 + spread(i, others.length, CHIP_W + 10), -sz.h / 2 + h + 60, flat ? 0 : -200, CHIP_W, CHIP_H, {
        id: s.id, title: s.name, sub: 'uses the same view model',
      }));
      links.push(link(`${key}:b`, `${ck}:t`, 'uses-viewmodel', '', { arrow: false }));
    });
  }

  if (lens === 'states') {
    const owned = G.statesOf(g, screenId).map((st) => ({ st, owner: null }));
    for (const { node: inst } of G.instancesOfScreen(g, screenId)) {
      for (const st of G.statesOf(g, inst.id)) owned.push({ st, owner: inst });
    }
    if (owned.length === 0) return { cards, links, note: 'No declared states for this screen' };
    const tsz = previewCardSize('sm');
    const cols = 2;
    const x0 = sz.w / 2 + 60 + tsz.w / 2;
    owned.forEach(({ st, owner }, j) => {
      const col = j % cols, row = Math.floor(j / cols);
      const rows = Math.ceil(owned.length / cols);
      cards.push(card('state', `${st.id}#thumb`, 'state', x0 + col * (tsz.w + 44), spread(row, rows, tsz.h + 16), flat ? 0 : 20 + col * 10, tsz.w, tsz.h, {
        id: st.id, screenId, size: 'sm', ownerName: owner?.name || '', highlightId: owner?.id || null, rotY: flat ? 0 : -6,
      }));
    });
  }

  if (lens === 'structure') {
    const insts = G.instancesOfScreen(g, screenId);
    const defs = new Map();
    for (const { node: inst } of insts) {
      const def = G.definitionOf(g, inst.id);
      if (def) defs.set(def.id, [...(defs.get(def.id) || []), inst]);
    }
    [...defs.entries()].forEach(([defId, instances], i) => {
      const def = G.node(g, defId);
      const uses = G.screensUsingDefinition(g, defId);
      const key = defId;
      cards.push(card('chip', key, 'component', spread(i, defs.size, 230), sz.h / 2 + 60, flat ? 0 : 80, CHIP_W + 40, CHIP_H, {
        id: defId, title: def.name, sub: `${uses.length} uses · ${uses.map((u) => u.screen.name).join(', ')}`,
      }));
      for (const inst of instances) links.push(link(`${front}/${inst.id}:c`, `${key}:t`, 'instance-of', 'instance of', { arrow: false }));
    });
    const note = defs.size === 0 ? `${insts.length} elements, all declared inline` : `${insts.length} elements · hover the preview to name them`;
    return { cards, links, note };
  }
  return { cards, links, note: '' };
}

// ------------------------------------------------------------------ component (instance or definition)
function layoutComponent(g, id, lens, flat, docked, trail) {
  const n = G.node(g, id);
  if (n.type === 'component') return layoutDefinition(g, n, flat, docked);
  const cards = [], links = [];
  const screen = G.hostScreenOf(g, id);
  const size = docked ? 'md' : 'lg';
  const sz = previewCardSize(size);
  const front = screen.id;
  cards.push(card('screen', front, 'screen', docked ? 0 : -60, 0, 0, sz.w, sz.h, { id: screen.id, size, interactive: !docked, highlightId: id, recede: true }));
  if (docked) return { cards, links, note: '' };
  const rx = sz.w / 2 + 160;
  const region = `${front}/${id}`;

  if (lens === 'behavior') {
    const vm = G.vmOfScreen(g, screen.id);
    const edges = G.outEdges(g, id).filter((e) => e.relation === 'binds-to' || e.relation === 'invokes');
    if (!vm || edges.length === 0) return { cards, links, note: 'No bindings or commands declared on this element' };
    const members = G.membersOfVm(g, vm.id);
    const hot = new Set(edges.map((e) => e.to));
    const b = n.preview?.bounds;
    const frac = new Map(members.filter((m) => hot.has(m.id)).map((m, i) => [m.id, b ? b.y + b.h / 2 + (i - (hot.size - 1) / 2) * 0.08 : 0.5]));
    const { rows, h } = alignedRows(members, hot, frac, FRAME[size][1]);
    cards.push(card('vm', vm.id, 'viewmodel', rx + 40, -sz.h / 2 + h / 2, flat ? 0 : -200, VM_W, h, {
      id: vm.id, members, rows, hot, faded: new Set(members.filter((m) => !hot.has(m.id)).map((m) => m.id)),
      tag: G.screensUsingVm(g, vm.id).length > 1 ? 'shared' : '',
    }));
    edges.forEach((e, i) => links.push(link(`${region}:r`, `${vm.id}/${e.to}:l`, e.relation, e.label || '', { arrow: e.relation === 'invokes', inferred: G.isInferred(e), labelT: edges.length === 1 ? 0.45 : 0.2 + 0.6 * (i / (edges.length - 1)) })));
    // what the hot members depend on, as a second column
    const deps = [];
    for (const m of members.filter((m) => hot.has(m.id))) for (const e of G.outEdges(g, m.id, 'depends-on')) deps.push(e);
    const depY0 = -sz.h / 2 + (rows.find((r) => r.hot)?.y || 0) + HEAD + 8;
    deps.forEach((e, i) => {
      const dep = G.node(g, e.to);
      const key = `${e.to}#dep`;
      if (cards.some((c) => c.key === key)) { links.push(link(`${vm.id}/${e.from}:r`, `${key}:l`, 'depends-on', e.label || '', { arrow: true, inferred: G.isInferred(e) })); return; }
      cards.push(card('chip', key, dep.type, rx + 40 + VM_W / 2 + 70 + (CHIP_W + 20) / 2, depY0 + i * (CHIP_H + 10), flat ? 0 : -200, CHIP_W + 20, CHIP_H, {
        id: dep.id, title: dep.name, sub: dep.properties?.clrType || '', mono: true,
      }));
      links.push(link(`${vm.id}/${e.from}:r`, `${key}:l`, 'depends-on', e.label || '', { arrow: true, inferred: G.isInferred(e) }));
    });
    return { cards, links, note: `${edges.length} binding${edges.length === 1 ? '' : 's'} on ${n.name}` };
  }

  if (lens === 'navigation') {
    const routes = G.routesFromInstance(g, id);
    if (routes.length === 0) return { cards, links, note: 'No navigation starts from this element' };
    routes.forEach((r, i) => {
      const key = `${r.route.id}#out`;
      cards.push(card('chip', key, 'screen', rx, spread(i, routes.length, CHIP_H + 12), 0, CHIP_W, CHIP_H, {
        id: r.target.id, title: r.target.name, sub: r.route.properties?.mechanism?.split('.')[0] || '', routeId: r.route.id,
      }));
      links.push(link(`${region}:r`, `${key}:l`, 'route', r.origin.edge.label || r.origin.via.name, { arrow: true, id: r.route.id }));
    });
    return { cards, links, note: '' };
  }

  if (lens === 'structure') {
    const def = G.definitionOf(g, id);
    const container = G.containerOf(g, id);
    if (!def) {
      cards.push(card('chip', `${container.id}#host`, container.type, rx, -40, 0, CHIP_W + 20, CHIP_H, {
        id: container.id, title: container.name, sub: 'contains this element',
      }));
      links.push(link(`${container.id}#host:l`, `${region}:r`, 'contains', 'contains', { arrow: true }));
      return { cards, links, note: `Declared inline in ${G.fileLine(g, n.source)?.path.split('/').pop() || 'the page'} · no shared definition` };
    }
    const siblings = G.instancesOf(g, def.id).filter((i) => i.id !== id);
    cards.push(card('chip', def.id, 'component', rx, -120, flat ? 0 : 60, CHIP_W + 40, CHIP_H, {
      id: def.id, title: def.name, sub: `definition · ${siblings.length + 1} uses`,
    }));
    links.push(link(`${region}:r`, `${def.id}:l`, 'instance-of', 'instance of', { arrow: true }));
    siblings.forEach((s, i) => {
      const host = G.hostScreenOf(g, s.id);
      const key = `${s.id}#sib`;
      cards.push(card('chip', key, 'component-instance', rx + 20, -20 + i * (CHIP_H + 10), flat ? 0 : 60, CHIP_W + 40, CHIP_H, {
        id: s.id, title: s.name, sub: `on ${host?.name}`,
      }));
      links.push(link(`${def.id}:b`, `${key}:t`, 'instance-of', '', { arrow: false }));
    });
    return { cards, links, note: `${def.name} is also used on ${siblings.map((s) => G.hostScreenOf(g, s.id)?.name).join(', ')}` };
  }

  if (lens === 'states') {
    const own = G.statesOf(g, id).map((st) => ({ st, label: `on ${n.name}` }));
    const screenStates = G.statesOf(g, screen.id).filter((st) => (st.preview?.dim || []).includes(id) || (st.preview?.hide || []).includes(id))
      .map((st) => ({ st, label: `screen state affects ${n.name}` }));
    const all = [...own, ...screenStates];
    if (all.length === 0) return { cards, links, note: `No declared states involve ${n.name}` };
    const tsz = previewCardSize('sm');
    all.forEach(({ st, label }, j) => {
      cards.push(card('state', `${st.id}#thumb`, 'state', rx - 40 + j * (tsz.w + 44), 0, flat ? 0 : 20 + j * 10, tsz.w, tsz.h, {
        id: st.id, screenId: screen.id, size: 'sm', ownerName: label, highlightId: id, rotY: flat ? 0 : -6,
      }));
      links.push(link(`${region}:r`, `${st.id}#thumb:l`, 'has-state', '', { arrow: false }));
    });
    return { cards, links, note: '' };
  }
  return { cards, links, note: '' };
}

function layoutDefinition(g, def, flat, docked) {
  const cards = [], links = [];
  const uses = G.screensUsingDefinition(g, def.id);
  cards.push(card('detail', def.id, 'component', 0, docked ? 0 : -180, flat ? 0 : 40, 320, 132, { id: def.id }));
  if (docked) return { cards, links, note: '' };
  const tsz = previewCardSize('sm');
  uses.forEach((u, i) => {
    const key = `${u.screen.id}#use`;
    cards.push(card('screen', key, 'screen', spread(i, uses.length, tsz.w + 40), 60 + tsz.h / 2 - 60, 0, tsz.w, tsz.h, {
      id: u.screen.id, size: 'sm', interactive: true, highlightId: u.instance.id, recede: true, caption: u.instance.name,
    }));
    links.push(link(`${def.id}:b`, `${key}/${u.instance.id}:c`, 'instance-of', '', { arrow: true }));
  });
  return { cards, links, note: `${uses.length} uses across ${uses.length} screen${uses.length === 1 ? '' : 's'}` };
}

// ------------------------------------------------------------------ detail (member, state, route, view model)
function layoutDetail(g, id, lens, flat, docked, trail) {
  const n = G.node(g, id);
  switch (n.type) {
    case 'viewmodel': return layoutViewModel(g, n, flat, docked);
    case 'state': return layoutState(g, n, flat, docked, trail);
    case 'route': return layoutRoute(g, n, flat, docked);
    default: return layoutMember(g, n, flat, docked, trail);
  }
}

function layoutViewModel(g, vm, flat, docked) {
  const cards = [], links = [];
  const members = G.membersOfVm(g, vm.id);
  const h = vmHeight(members.length);
  cards.push(card('vm', vm.id, 'viewmodel', 0, 0, 0, VM_W + 30, h, { id: vm.id, members, hot: new Set(), faded: new Set(), expanded: true }));
  if (docked) return { cards, links, note: '' };
  const users = G.screensUsingVm(g, vm.id);
  const tsz = previewCardSize('sm');
  users.forEach((s, i) => {
    const key = `${s.id}#user`;
    cards.push(card('screen', key, 'screen', -(VM_W / 2 + 200), spread(i, users.length, tsz.h + 16), flat ? 0 : 60, tsz.w, tsz.h, { id: s.id, size: 'sm', interactive: false }));
    links.push(link(`${key}:r`, `${vm.id}:l`, 'uses-viewmodel', 'uses', { arrow: true }));
  });
  return { cards, links, note: users.length > 1 ? `Shared: serves ${users.map((s) => s.name).join(' and ')}` : '' };
}

function layoutMember(g, m, flat, docked, trail) {
  const cards = [], links = [];
  const W = 320, H = 190;
  cards.push(card('detail', m.id, m.type, 0, 0, 0, W, H, { id: m.id }));
  if (docked) return { cards, links, note: '' };
  const vm = G.vmOfMember(g, m.id);
  if (vm) {
    cards.push(card('chip', `${vm.id}#owner`, 'viewmodel', 40, -(H / 2 + 80), flat ? 0 : -160, CHIP_W + 30, CHIP_H, { id: vm.id, title: vm.name, sub: 'exposes this member' }));
    links.push(link(`${vm.id}#owner:b`, `${m.id}:t`, 'exposes', '', { arrow: true }));
  }
  // consumers: the UI that binds to or invokes the member, shown on their screens
  const consumers = G.consumersOfMember(g, m.id);
  const byScreen = new Map();
  for (const c of consumers) {
    if (!c.screen) continue;
    if (!byScreen.has(c.screen.id)) byScreen.set(c.screen.id, []);
    byScreen.get(c.screen.id).push(c);
  }
  const tsz = previewCardSize('sm');
  const screens = [...byScreen.entries()];
  screens.forEach(([screenId, cs], i) => {
    const key = `${screenId}#ctx`;
    cards.push(card('screen', key, 'screen', -(W / 2 + 200), spread(i, screens.length, tsz.h + 16), 0, tsz.w, tsz.h, {
      id: screenId, size: 'sm', interactive: true, highlightId: cs[0].instance.id, highlightIds: cs.map((c) => c.instance.id), recede: true,
    }));
    for (const c of cs) links.push(link(`${key}/${c.instance.id}:r`, `${m.id}:l`, c.edge.relation, c.edge.label || '', { arrow: c.edge.relation === 'invokes', inferred: G.isInferred(c.edge) }));
  });
  // dependencies (what it reads) above right, dependents (what reads it) below right
  const deps = G.outEdges(g, m.id, 'depends-on');
  const dependents = G.inEdges(g, m.id, 'depends-on');
  const routes = G.outEdges(g, m.id, 'entered-via');
  const transitions = G.outEdges(g, m.id, 'transitions-to');
  const rightCol = [
    ...deps.map((e) => ({ e, dir: 'out', node: G.node(g, e.to), head: 'reads' })),
    ...transitions.map((e) => ({ e, dir: 'out', node: G.node(g, e.to), head: 'transitions to' })),
    ...routes.map((e) => ({ e, dir: 'out', node: G.node(g, e.to), head: 'navigates' })),
    ...dependents.map((e) => ({ e, dir: 'in', node: G.node(g, e.from), head: 'read by' })),
  ];
  rightCol.forEach((item, i) => {
    const key = `${item.node.id}#${item.dir}`;
    if (cards.some((c) => c.key === key)) return;
    const route = item.node.type === 'route' ? G.routeTarget(g, item.node.id) : null;
    cards.push(card('chip', key, item.node.type, W / 2 + 180, spread(i, rightCol.length, CHIP_H + 10), 0, CHIP_W + 30, CHIP_H, {
      id: item.node.id, title: route ? `→ ${route.name}` : item.node.name, sub: item.head + (item.node.type === 'state' ? ` · state` : ''), mono: item.node.type === 'property' || item.node.type === 'command',
    }));
    if (item.dir === 'out') links.push(link(`${m.id}:r`, `${key}:l`, item.e.relation, item.e.label || '', { arrow: true, inferred: G.isInferred(item.e), labelT: 0.35 }));
    else links.push(link(`${key}:l`, `${m.id}:r`, item.e.relation, item.e.label || '', { arrow: true, inferred: G.isInferred(item.e), labelT: 0.65 }));
  });
  const note = consumers.length ? `${consumers.length} binding${consumers.length === 1 ? '' : 's'} · ${deps.length} dependenc${deps.length === 1 ? 'y' : 'ies'} · ${dependents.length} dependent${dependents.length === 1 ? '' : 's'}` : 'Not bound by any UI element';
  return { cards, links, note };
}

function layoutState(g, st, flat, docked, trail) {
  const cards = [], links = [];
  const owner = G.ownerOfState(g, st.id);
  const screen = owner ? (owner.type === 'screen' ? owner : G.hostScreenOf(g, owner.id)) : null;
  const size = docked ? 'sm' : 'md';
  const sz = previewCardSize(size);
  const key = `${st.id}#big`;
  cards.push(card('state', key, 'state', 0, 0, 0, sz.w, sz.h, { id: st.id, screenId: screen?.id, size, ownerName: owner?.type === 'component-instance' ? `on ${owner.name}` : '', highlightId: owner?.type === 'component-instance' ? owner.id : null, big: true }));
  if (docked) return { cards, links, note: '' };
  const drivers = G.outEdges(g, st.id, 'depends-on');
  drivers.forEach((e, i) => {
    const d = G.node(g, e.to);
    const ck = `${d.id}#driver`;
    cards.push(card('chip', ck, d.type, sz.w / 2 + 170, spread(i, drivers.length, CHIP_H + 10) - 60, flat ? 0 : -160, CHIP_W + 30, CHIP_H, { id: d.id, title: d.name, sub: `${d.properties?.clrType || ''}${e.label ? ' · ' + e.label : ''}`, mono: true }));
    links.push(link(`${key}:r`, `${ck}:l`, 'depends-on', 'driven by', { arrow: true, inferred: G.isInferred(e) }));
  });
  const ins = G.inEdges(g, st.id, 'transitions-to');
  const outs = G.outEdges(g, st.id, 'transitions-to');
  ins.forEach((e, i) => {
    const s = G.node(g, e.from);
    const ck = `${s.id}#from`;
    cards.push(card('chip', ck, s.type, -(sz.w / 2 + 170), spread(i, ins.length, CHIP_H + 10), 0, CHIP_W + 20, CHIP_H, { id: s.id, title: s.name, sub: e.label || 'transitions here' }));
    links.push(link(`${ck}:r`, `${key}:l`, 'transitions-to', e.label || '', { arrow: true }));
  });
  outs.forEach((e, i) => {
    const s = G.node(g, e.to);
    const ck = `${s.id}#to`;
    cards.push(card('chip', ck, s.type, sz.w / 2 + 170, 60 + i * (CHIP_H + 10) + (drivers.length ? 40 : 0), 0, CHIP_W + 20, CHIP_H, { id: s.id, title: s.name, sub: e.label || 'next' }));
    links.push(link(`${key}:r`, `${ck}:l`, 'transitions-to', e.label || '', { arrow: true }));
  });
  const trigger = st.properties?.trigger;
  return { cards, links, note: trigger ? `when ${trigger}` : '' };
}

function layoutRoute(g, r, flat, docked) {
  const cards = [], links = [];
  cards.push(card('detail', r.id, 'route', 0, 0, 0, 280, 130, { id: r.id }));
  if (docked) return { cards, links, note: '' };
  const tsz = previewCardSize('sm');
  const origins = G.routeOrigins(g, r.id);
  origins.forEach((o, i) => {
    const key = `${o.screen.id}#origin`;
    cards.push(card('screen', key, 'screen', -(140 + 160), spread(i, origins.length, tsz.h + 16), 0, tsz.w, tsz.h, { id: o.screen.id, size: 'sm', interactive: true, highlightId: o.instance?.id || null, recede: !!o.instance }));
    links.push(link(o.instance ? `${key}/${o.instance.id}:r` : `${key}:r`, `${r.id}:l`, 'route', o.edge.label || o.via.name, { arrow: true }));
  });
  const target = G.routeTarget(g, r.id);
  if (target) {
    const key = `${target.id}#target`;
    cards.push(card('screen', key, 'screen', 140 + 160, 0, 0, tsz.w, tsz.h, { id: target.id, size: 'sm', interactive: false }));
    links.push(link(`${r.id}:r`, `${key}:l`, 'route', 'navigates to', { arrow: true }));
  }
  return { cards, links, note: r.properties?.mechanism || '' };
}

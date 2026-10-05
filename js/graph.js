// Load, index and query an App Graph. Pure functions over the indexed graph.

export async function loadGraph(url) {
  const res = await fetch(url, { cache: 'no-cache' });
  if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
  return indexGraph(await res.json());
}

export function indexGraph(raw) {
  const byId = new Map(raw.nodes.map((n) => [n.id, n]));
  const out = new Map();
  const inc = new Map();
  for (const e of raw.edges) {
    if (!out.has(e.from)) out.set(e.from, []);
    if (!inc.has(e.to)) inc.set(e.to, []);
    out.get(e.from).push(e);
    inc.get(e.to).push(e);
  }
  const byType = new Map();
  for (const n of raw.nodes) {
    if (!byType.has(n.type)) byType.set(n.type, []);
    byType.get(n.type).push(n);
  }
  const files = new Map((raw.files || []).map((f) => [f.id, f]));
  return { raw, byId, out, inc, byType, files };
}

// ---- primitive queries ----
export const node = (g, id) => g.byId.get(id);
export const nodesOf = (g, type) => g.byType.get(type) || [];
export const outEdges = (g, id, relation) => (g.out.get(id) || []).filter((e) => !relation || e.relation === relation);
export const inEdges = (g, id, relation) => (g.inc.get(id) || []).filter((e) => !relation || e.relation === relation);
export const targets = (g, id, relation) => outEdges(g, id, relation).map((e) => node(g, e.to));
export const sources = (g, id, relation) => inEdges(g, id, relation).map((e) => node(g, e.from));
export const isInferred = (e) => e?.evidence?.kind === 'inferred';

// ---- structural helpers ----
export const featureOfScreen = (g, screenId) => targets(g, screenId, 'belongs-to')[0] || null;
export const screensOfFeature = (g, featureId) => sources(g, featureId, 'belongs-to');
export const vmOfScreen = (g, screenId) => targets(g, screenId, 'uses-viewmodel')[0] || null;
export const screensUsingVm = (g, vmId) => sources(g, vmId, 'uses-viewmodel');
export const membersOfVm = (g, vmId) => targets(g, vmId, 'exposes');
export const vmOfMember = (g, memberId) => sources(g, memberId, 'exposes')[0] || null;
export const definitionOf = (g, instId) => targets(g, instId, 'instance-of')[0] || null;
export const instancesOf = (g, defId) => sources(g, defId, 'instance-of');
export const statesOf = (g, ownerId) => targets(g, ownerId, 'has-state');
export const ownerOfState = (g, stateId) => sources(g, stateId, 'has-state')[0] || null;
export const containerOf = (g, instId) => sources(g, instId, 'contains')[0] || null;

export function hostScreenOf(g, id) {
  let cur = node(g, id);
  let guard = 0;
  while (cur && cur.type !== 'screen' && guard++ < 10) cur = containerOf(g, cur.id);
  return cur && cur.type === 'screen' ? cur : null;
}

/** All component instances in a screen, depth first, with their depth. */
export function instancesOfScreen(g, screenId) {
  const acc = [];
  const walk = (id, depth) => {
    for (const child of targets(g, id, 'contains')) {
      acc.push({ node: child, depth });
      walk(child.id, depth + 1);
    }
  };
  walk(screenId, 0);
  return acc;
}

/** Screens where a definition is used, through its instances. */
export function screensUsingDefinition(g, defId) {
  const seen = new Map();
  for (const inst of instancesOf(g, defId)) {
    const screen = hostScreenOf(g, inst.id);
    if (screen && !seen.has(screen.id)) seen.set(screen.id, { screen, instance: inst });
  }
  return [...seen.values()];
}

/** Instances that bind to or invoke a member, with their host screens. */
export function consumersOfMember(g, memberId) {
  return inEdges(g, memberId)
    .filter((e) => e.relation === 'binds-to' || e.relation === 'invokes')
    .map((e) => ({ edge: e, instance: node(g, e.from), screen: hostScreenOf(g, e.from) }));
}

// ---- routes ----
/** Where a route originates: the UI element and the screen it lives on. */
export function routeOrigins(g, routeId) {
  const origins = [];
  for (const e of inEdges(g, routeId, 'entered-via')) {
    const via = node(g, e.from);
    if (!via) continue;
    if (via.type === 'command') {
      // a command fires the route; the screens are those whose instances invoke it
      const invokers = sources(g, via.id, 'invokes');
      if (invokers.length === 0) {
        for (const s of screensUsingVm(g, vmOfMember(g, via.id)?.id)) origins.push({ screen: s, via, edge: e, instance: null });
      }
      for (const inst of invokers) origins.push({ screen: hostScreenOf(g, inst.id), via, edge: e, instance: inst });
    } else {
      origins.push({ screen: hostScreenOf(g, via.id) || (via.type === 'screen' ? via : null), via, edge: e, instance: via.type === 'component-instance' ? via : null });
    }
  }
  return origins.filter((o) => o.screen);
}

export const routeTarget = (g, routeId) => targets(g, routeId, 'navigates-to')[0] || null;

export function routesFrom(g, screenId) {
  const acc = [];
  for (const r of nodesOf(g, 'route')) {
    for (const o of routeOrigins(g, r.id)) {
      if (o.screen.id === screenId) acc.push({ route: r, origin: o, target: routeTarget(g, r.id) });
    }
  }
  return acc;
}

export function routesTo(g, screenId) {
  const acc = [];
  for (const r of nodesOf(g, 'route')) {
    const t = routeTarget(g, r.id);
    if (t?.id !== screenId) continue;
    for (const o of routeOrigins(g, r.id)) acc.push({ route: r, origin: o, target: t });
  }
  return acc;
}

/** Routes an instance (or the command it invokes) starts. */
export function routesFromInstance(g, instId) {
  const acc = [];
  for (const r of nodesOf(g, 'route')) {
    for (const o of routeOrigins(g, r.id)) {
      if (o.instance?.id === instId) acc.push({ route: r, origin: o, target: routeTarget(g, r.id) });
    }
  }
  return acc;
}

// ---- levels and context ----
export function levelOf(g, id) {
  if (!id) return 'application';
  const n = node(g, id);
  if (!n) return 'application';
  switch (n.type) {
    case 'feature': return 'feature';
    case 'screen': return 'screen';
    case 'component':
    case 'component-instance': return 'component';
    default: return 'detail';
  }
}

/** Parent for zoom-out. Prefers a parent present in the trail. */
export function parentOf(g, id, trail = []) {
  const n = node(g, id);
  if (!n) return null;
  const prefer = (candidates) => {
    const list = candidates.filter(Boolean);
    if (list.length === 0) return null;
    for (let i = trail.length - 1; i >= 0; i--) {
      const hit = list.find((c) => c.id === trail[i]);
      if (hit) return hit;
    }
    return list[0];
  };
  switch (n.type) {
    case 'feature': return null;
    case 'screen': return featureOfScreen(g, id);
    case 'component-instance': return containerOf(g, id);
    case 'component': return prefer(screensUsingDefinition(g, id).map((u) => u.instance)) || null;
    case 'viewmodel': return prefer(screensUsingVm(g, id));
    case 'property':
    case 'command': {
      const consumers = consumersOfMember(g, id).map((c) => c.instance);
      return prefer([...consumers, vmOfMember(g, id)]);
    }
    case 'state': return ownerOfState(g, id);
    case 'route': return prefer(routeOrigins(g, id).map((o) => o.instance || o.screen));
    default: return null;
  }
}

/** Breadcrumb chain from the application down to id. */
export function contextChain(g, id, trail = []) {
  const chain = [];
  let cur = id;
  let guard = 0;
  while (cur && guard++ < 12) {
    chain.unshift(node(g, cur));
    const p = parentOf(g, cur, trail);
    cur = p?.id || null;
  }
  return chain;
}

/** Screen that gives spatial context to an entity (for previews at detail level). */
export function contextScreen(g, id, trail = []) {
  const chain = contextChain(g, id, trail);
  return chain.find((n) => n.type === 'screen') || null;
}

// ---- search ----
export function search(g, query, limit = 12) {
  const q = query.trim().toLowerCase();
  if (!q) return [];
  const score = (n) => {
    const name = n.name.toLowerCase();
    if (name === q) return 0;
    if (name.startsWith(q)) return 1;
    if (name.includes(q)) return 2;
    if (n.id.includes(q)) return 3;
    if ((n.summary || '').toLowerCase().includes(q)) return 4;
    return null;
  };
  return g.raw.nodes
    .map((n) => ({ n, s: score(n) }))
    .filter((x) => x.s !== null)
    .sort((a, b) => a.s - b.s || a.n.name.localeCompare(b.n.name))
    .slice(0, limit)
    .map((x) => x.n);
}

export const TYPE_LABEL = {
  feature: 'Feature', screen: 'Screen', component: 'Component', 'component-instance': 'Instance',
  viewmodel: 'View model', property: 'Property', command: 'Command', state: 'State', route: 'Route',
};
export const TYPE_GLYPH = {
  feature: '▣', screen: '▯', component: '▭', 'component-instance': '▭', viewmodel: '◆', property: '◇', command: '◈', state: '◐', route: '➝',
};

export function fileLine(g, ref) {
  if (!ref) return null;
  const f = g.files.get(ref.file);
  if (!f) return null;
  const i = ref.line - f.startLine;
  return { file: f, text: f.lines[i] ?? '', path: f.path };
}

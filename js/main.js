// Boot: load the graph, wire the store to the scene, inspector, editor, breadcrumb, search and keyboard.

import { createStore, initialState, loadPrefs, savePrefs, LENSES } from './store.js';
import * as G from './graph.js';
import { layout } from './layout.js';
import { createScene } from './scene.js';
import { renderInspector } from './inspector.js';
import { createEditor } from './editor.js';
import { glyph } from './icons.js';
import { setFidelity } from './preview.js';

const $ = (id) => document.getElementById(id);

async function boot() {
  let graph;
  try {
    graph = await G.loadGraph('graph/orderly.graph.json');
  } catch (err) {
    $('boot-error').hidden = false;
    console.error(err);
    return;
  }
  const prefs = loadPrefs();
  const media = window.matchMedia('(prefers-reduced-motion: reduce)');
  const store = createStore(initialState(graph, { ...prefs, reducedMotion: prefs.reducedMotion ?? media.matches }));

  const els = { viewer: $('viewer'), stage: $('stage'), world: $('world'), svg: $('links') };
  const scene = createScene(els, {
    onFocus: (id) => focus(id),
    onHover: (id) => store.dispatch((s) => (s.hoverId === id ? s : { ...s, hoverId: id })),
    onCursor: (id) => store.dispatch((s) => (s.cursorId === id ? s : { ...s, cursorId: id })),
    onZoomIn: (id) => focus(id),
    onZoomOut: () => zoomOut(),
    onLayoutChange: (moved) => { $('layout-reset').hidden = !moved; },
    onCamera: (cam, S) => { $('camera-readout').textContent = `yaw ${cam.yaw.toFixed(0)}° · pitch ${cam.pitch.toFixed(0)}° · ${Math.round(S * 100)}%`; },
  });
  const editor = createEditor($('editor-files'), $('editor-code'), {
    openFile: (fileId) => store.dispatch((s) => ({ ...s, editor: { fileId, line: s.editor.fileId === fileId ? s.editor.line : null } })),
    focusFromEditor: (id, line) => focus(id, { line }),
  });
  editor.setGraph(graph);

  // ---------- actions ----------
  function focus(id, opts = {}) {
    store.dispatch((s) => {
      if (id === s.focusId && !opts.line) return s;
      const n = id ? G.node(graph, id) : null;
      const trail = id ? [...s.trail.filter((t) => t !== id), id].slice(-24) : s.trail;
      const editorState = n?.source ? { fileId: n.source.file, line: opts.line ?? n.source.line } : s.editor;
      let lens = s.lens;
      if (opts.lens) lens = opts.lens;
      return { ...s, focusId: id, trail, editor: editorState, lens, cursorId: null, searchOpen: false, sceneVersion: s.sceneVersion + 1 };
    });
  }
  function zoomOut() {
    const s = store.get();
    if (!s.focusId) return;
    const p = G.parentOf(graph, s.focusId, s.trail);
    focus(p ? p.id : null);
  }
  function back() {
    const t = store.get().trail;
    if (t.length < 2) { focus(null); return; }
    const prev = t[t.length - 2];
    store.dispatch((s) => ({ ...s, trail: s.trail.slice(0, -1) }));
    focus(prev);
  }
  function zoomIn() {
    const s = store.get();
    const id = s.cursorId || s.hoverId;
    if (id && id !== s.focusId) focus(id);
  }
  const setLens = (lens) => store.dispatch((s) => (s.lens === lens ? s : { ...s, lens, sceneVersion: s.sceneVersion + 1 }));
  const toggleView = () => store.dispatch((s) => ({ ...s, view: s.view === 'orbit' ? 'flat' : 'orbit', sceneVersion: s.sceneVersion + 1 }));
  const toggleMode = () => store.dispatch((s) => ({ ...s, mode: s.mode === 'docked' ? 'expanded' : 'docked', sceneVersion: s.sceneVersion + 1 }));
  const toggleMotion = () => store.dispatch((s) => ({ ...s, reducedMotion: !s.reducedMotion }));
  const toggleFidelity = () => store.dispatch((s) => ({ ...s, fidelity: s.fidelity === 'ui' ? 'wire' : 'ui', sceneVersion: s.sceneVersion + 1 }));

  const inspectorActions = {
    focus: (id, lens) => focus(id, lens ? { lens } : {}),
    focusLens: (id) => focus(id, { lens: 'structure' }),
    hover: (id) => store.dispatch((s) => ({ ...s, hoverId: id })),
    openSource: (n) => store.dispatch((s) => ({ ...s, editor: { fileId: n.source.file, line: n.source.line } })),
    openSourceRef: (ref) => store.dispatch((s) => ({ ...s, editor: { fileId: ref.file, line: ref.line } })),
  };

  // ---------- render ----------
  let lastScene = -1, lastMode, lastView, lastLens;
  store.subscribe((s, prev) => {
    setFidelity(s.fidelity);
    $('toggle-fidelity').setAttribute('aria-pressed', String(s.fidelity === 'ui'));
    $('toggle-fidelity').textContent = s.fidelity === 'ui' ? 'UI' : 'Wire';
    document.body.dataset.mode = s.mode;
    document.body.dataset.view = s.view;
    document.body.dataset.lens = s.lens;
    document.documentElement.dataset.reducedMotion = String(s.reducedMotion);
    $('toggle-view').setAttribute('aria-pressed', String(s.view === 'flat'));
    $('toggle-mode').setAttribute('aria-pressed', String(s.mode === 'docked'));
    $('toggle-motion').setAttribute('aria-pressed', String(s.reducedMotion));
    for (const b of $('lens-tabs').querySelectorAll('[data-lens]')) b.setAttribute('aria-selected', String(b.dataset.lens === s.lens));

    if (s.sceneVersion !== lastScene || s.mode !== lastMode || s.view !== lastView || s.lens !== lastLens) {
      const l = layout(s);
      scene.render(s, l);
      $('viewer-note').textContent = l.note || '';
      const focusNode = s.focusId ? G.node(graph, s.focusId) : null;
      $('viewer-title').textContent = focusNode ? focusNode.name : graph.raw.name;
      lastScene = s.sceneVersion; lastMode = s.mode; lastView = s.view; lastLens = s.lens;
      $('viewer-back').disabled = s.trail.length === 0;
      renderBreadcrumb(s);
      renderInspector($('inspector'), s, inspectorActions);
    } else if (s.hoverId !== prev.hoverId || s.cursorId !== prev.cursorId) {
      scene.updateHighlights(s);
    }
    if (s.editor !== prev.editor || s.focusId !== prev.focusId || s.sceneVersion !== prev.sceneVersion) editor.render(s);
    if (s.searchOpen !== prev.searchOpen && !s.searchOpen) closeSearch();
    if (s.lens !== prev.lens || s.mode !== prev.mode || s.view !== prev.view || s.reducedMotion !== prev.reducedMotion || s.fidelity !== prev.fidelity || s.workspaceRoot !== prev.workspaceRoot) savePrefs(s);
    if (s.focusId !== prev.focusId) history.replaceState(null, '', s.focusId ? `#${s.focusId}` : location.pathname + location.search);
  });

  function renderBreadcrumb(s) {
    const nav = $('breadcrumb');
    nav.innerHTML = '';
    const chain = s.focusId ? G.contextChain(graph, s.focusId, s.trail) : [];
    const crumbs = [{ id: null, name: graph.raw.name, type: null }, ...chain];
    const collapsed = crumbs.length > 4 ? [crumbs[0], { ellipsis: true, hidden: crumbs.slice(1, -2) }, ...crumbs.slice(-2)] : crumbs;
    collapsed.forEach((c, i) => {
      if (i) nav.appendChild(Object.assign(document.createElement('span'), { className: 'sep', textContent: '›' }));
      if (c.ellipsis) {
        const e = document.createElement('button');
        e.type = 'button'; e.textContent = '…'; e.title = c.hidden.map((x) => x.name).join(' › ');
        e.addEventListener('click', () => focus(c.hidden[c.hidden.length - 1].id));
        nav.appendChild(e);
        return;
      }
      const b = document.createElement('button');
      b.type = 'button';
      if (c.type) b.appendChild(glyph(c.type, 'crumb-type'));
      b.append(c.name);
      if (i === collapsed.length - 1) b.setAttribute('aria-current', 'location');
      b.addEventListener('click', () => focus(c.id));
      nav.appendChild(b);
    });
  }

  // ---------- search ----------
  const input = $('search-input'), results = $('search-results');
  let selected = 0, hits = [];
  function renderSearch() {
    results.innerHTML = '';
    results.hidden = false;
    if (!hits.length) {
      const li = document.createElement('li'); li.className = 'empty'; li.textContent = input.value.trim() ? 'No entity matches' : 'Type to search screens, members, states…';
      results.appendChild(li);
      return;
    }
    hits.forEach((n, i) => {
      const li = document.createElement('li');
      li.setAttribute('role', 'option');
      li.setAttribute('aria-selected', String(i === selected));
      const t = document.createElement('span'); t.className = 'r-type'; t.appendChild(glyph(n.type)); t.append(` ${G.TYPE_LABEL[n.type]}`);
      const name = document.createElement('span'); name.textContent = n.name;
      const ctx = document.createElement('span'); ctx.className = 'r-ctx';
      const chain = G.contextChain(graph, n.id); ctx.textContent = chain.slice(0, -1).map((c) => c.name).join(' › ');
      li.append(t, name, ctx);
      li.addEventListener('mousedown', (e) => { e.preventDefault(); pick(n.id); });
      results.appendChild(li);
    });
  }
  function pick(id) { focus(id); closeSearch(); input.blur(); }
  function closeSearch() { results.hidden = true; }
  input.addEventListener('input', () => { hits = G.search(graph, input.value); selected = 0; renderSearch(); });
  input.addEventListener('focus', () => { hits = G.search(graph, input.value); renderSearch(); });
  input.addEventListener('blur', () => setTimeout(closeSearch, 120));
  input.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowDown') { selected = Math.min(hits.length - 1, selected + 1); renderSearch(); e.preventDefault(); }
    else if (e.key === 'ArrowUp') { selected = Math.max(0, selected - 1); renderSearch(); e.preventDefault(); }
    else if (e.key === 'Enter' && hits[selected]) pick(hits[selected].id);
    else if (e.key === 'Escape') { input.value = ''; closeSearch(); input.blur(); }
  });

  // ---------- controls ----------
  $('lens-tabs').addEventListener('click', (e) => { const b = e.target.closest('[data-lens]'); if (b) setLens(b.dataset.lens); });
  $('toggle-view').addEventListener('click', toggleView);
  $('toggle-mode').addEventListener('click', toggleMode);
  $('viewer-expand').addEventListener('click', toggleMode);
  $('toggle-motion').addEventListener('click', toggleMotion);
  $('toggle-fidelity').addEventListener('click', toggleFidelity);
  $('layout-reset').addEventListener('click', () => scene.resetOffsets());
  $('toggle-help').addEventListener('click', () => { $('workspace-root').value = store.get().workspaceRoot; $('help').showModal(); });
  $('workspace-root').addEventListener('change', (e) => store.dispatch((s) => ({ ...s, workspaceRoot: e.target.value.trim(), sceneVersion: s.sceneVersion + 1 })));
  $('camera-reset').addEventListener('click', () => scene.resetCamera());
  $('viewer-back').addEventListener('click', back);
  media.addEventListener('change', (e) => store.dispatch((s) => ({ ...s, reducedMotion: e.matches })));

  document.addEventListener('keydown', (e) => {
    const typing = e.target.matches('input, textarea, [contenteditable]');
    if (typing) return;
    if ($('help').open) { if (e.key === 'Escape') $('help').close(); return; }
    const inViewer = $('viewer').contains(document.activeElement) || document.activeElement === document.body;
    switch (e.key) {
      case '/': e.preventDefault(); input.focus(); input.select(); break;
      case '?': $('help').showModal(); break;
      case '+': case '=': zoomIn(); break;
      case '-': case '_': case 'Backspace': if (!typing) { e.preventDefault(); zoomOut(); } break;
      case 'Escape': focus(store.get().focusId); break;
      case '0': scene.resetCamera(); break;
      case '1': case '2': case '3': case '4': setLens(LENSES[Number(e.key) - 1]); break;
      case 'f': case 'F': toggleView(); break;
      case 'd': case 'D': toggleMode(); break;
      case 'w': case 'W': toggleFidelity(); break;
      case 'ArrowLeft': case 'ArrowRight': case 'ArrowUp': case 'ArrowDown': {
        const dx = e.key === 'ArrowLeft' ? -1 : e.key === 'ArrowRight' ? 1 : 0;
        const dy = e.key === 'ArrowUp' ? -1 : e.key === 'ArrowDown' ? 1 : 0;
        if (e.altKey && dx < 0) { e.preventDefault(); back(); break; }
        if (e.shiftKey) {
          const id = store.get().cursorId || store.get().focusId;
          if (id && scene.nudgeCard(id, dx * 8, dy * 8)) e.preventDefault();
          break;
        }
        if (inViewer && store.get().view === 'orbit') { e.preventDefault(); scene.nudge(dx * 6, -dy * 4); }
        break;
      }
      default: return;
    }
  });

  // expose for tests
  window.appOrbit = { store, focus, zoomOut, back, setLens, toggleView, toggleMode, toggleFidelity, scene, graph, layout: () => layout(store.get()) };

  // first paint, honouring a #entity-id deep link
  const hashId = decodeURIComponent(location.hash.slice(1));
  if (hashId && G.node(graph, hashId)) store.dispatch((s) => ({ ...s, focusId: hashId, trail: [hashId], editor: G.node(graph, hashId).source ? { fileId: G.node(graph, hashId).source.file, line: G.node(graph, hashId).source.line } : s.editor, sceneVersion: 1 }));
  else store.dispatch((s) => ({ ...s, sceneVersion: 1 }));
  window.addEventListener('hashchange', () => { const id = decodeURIComponent(location.hash.slice(1)); if (id && G.node(graph, id) && id !== store.get().focusId) focus(id); });
  scene.resetCamera(store.get().view);
}

boot();

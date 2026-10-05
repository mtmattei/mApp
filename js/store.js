// Single state object. One writer: dispatch. Everything else derives from state.

export function createStore(initial) {
  let state = initial;
  const subs = new Set();
  return {
    get: () => state,
    dispatch(patch) {
      const next = typeof patch === 'function' ? patch(state) : { ...state, ...patch };
      if (next === state) return;
      const prev = state;
      state = next;
      for (const fn of subs) fn(state, prev);
    },
    subscribe(fn) {
      subs.add(fn);
      return () => subs.delete(fn);
    },
  };
}

export const LENSES = ['structure', 'navigation', 'behavior', 'states'];

export function initialState(graph, prefs) {
  return {
    graph,
    focusId: null,
    hoverId: null,
    cursorId: null,
    lens: LENSES.includes(prefs.lens) ? prefs.lens : 'structure',
    mode: prefs.mode === 'docked' ? 'docked' : 'expanded',
    view: prefs.view === 'flat' ? 'flat' : 'orbit',
    reducedMotion: !!prefs.reducedMotion,
    trail: [],
    runtime: { connected: false },
    editor: { fileId: null, line: null },
    searchOpen: false,
    sceneVersion: 0,
  };
}

export function loadPrefs() {
  try {
    return JSON.parse(localStorage.getItem('app-orbit.prefs') || '{}') || {};
  } catch {
    return {};
  }
}

export function savePrefs(state) {
  try {
    const { lens, mode, view, reducedMotion } = state;
    localStorage.setItem('app-orbit.prefs', JSON.stringify({ lens, mode, view, reducedMotion }));
  } catch {
    /* storage unavailable: preferences are per session */
  }
}

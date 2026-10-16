// Mock editor: file list and a code pane built from graph.files. Lines inside an entity's
// source range are linked; clicking one focuses the most specific entity. Focus moves the cursor here.

import * as G from './graph.js';

const h = (tag, cls, text) => {
  const el = document.createElement(tag);
  if (cls) el.className = cls;
  if (text != null) el.textContent = text;
  return el;
};

export function createEditor(filesEl, codeEl, actions) {
  let graph = null;
  let rangesByFile = new Map();
  let rendered = { fileId: undefined };

  function setGraph(g) {
    graph = g;
    rangesByFile = new Map();
    for (const n of g.raw.nodes) {
      if (!n.source) continue;
      const list = rangesByFile.get(n.source.file) || [];
      list.push({ id: n.id, start: n.source.line, end: n.source.endLine || n.source.line });
      rangesByFile.set(n.source.file, list);
    }
    renderFiles(null);
  }

  function renderFiles(activeId) {
    filesEl.innerHTML = '';
    const groups = new Map();
    for (const f of graph.raw.files) {
      const dir = f.path.split('/').slice(0, -1).join('/');
      if (!groups.has(dir)) groups.set(dir, []);
      groups.get(dir).push(f);
    }
    for (const [dir, files] of groups) {
      filesEl.appendChild(h('div', 'group', dir));
      for (const f of files) {
        const b = h('button', 'file');
        b.type = 'button';
        b.setAttribute('role', 'option');
        b.setAttribute('aria-selected', String(f.id === activeId));
        b.dataset.file = f.id;
        const name = f.path.split('/').pop();
        b.appendChild(h('span', null, name.replace(/\.(xaml|cs)$/, '')));
        b.appendChild(h('span', 'ext', name.match(/\.(xaml\.cs|xaml|cs)$/)?.[0] || ''));
        b.addEventListener('click', () => actions.openFile(f.id));
        filesEl.appendChild(b);
      }
    }
  }

  function render(state) {
    const { fileId, line } = state.editor;
    for (const b of filesEl.querySelectorAll('.file')) b.setAttribute('aria-selected', String(b.dataset.file === fileId));
    if (!fileId) {
      if (rendered.fileId !== null) { codeEl.innerHTML = ''; codeEl.appendChild(h('div', 'empty', 'Select an entity, or a file, to see its source.')); rendered = { fileId: null }; }
      return;
    }
    const f = graph.files.get(fileId);
    if (!f) return;
    if (rendered.fileId !== fileId) {
      codeEl.innerHTML = '';
      codeEl.appendChild(h('div', 'path', f.path));
      const ranges = rangesByFile.get(fileId) || [];
      f.lines.forEach((text, i) => {
        const ln = f.startLine + i;
        const row = h('div', 'line');
        row.dataset.line = ln;
        const covering = ranges.filter((r) => ln >= r.start && ln <= r.end).sort((a, b) => (a.end - a.start) - (b.end - b.start));
        if (covering.length) {
          row.classList.add('linked');
          row.dataset.id = covering[0].id;
          row.title = covering.map((c) => G.node(graph, c.id)?.name).join(' › ');
          row.addEventListener('click', () => actions.focusFromEditor(covering[0].id, ln));
        }
        row.appendChild(h('span', 'n', String(ln)));
        row.appendChild(h('span', 'm', covering.length ? '·' : ''));
        row.appendChild(h('span', 'c', text || ' '));
        codeEl.appendChild(row);
      });
      rendered = { fileId };
    }
    const focusNode = state.focusId ? G.node(graph, state.focusId) : null;
    const range = focusNode?.source?.file === fileId ? focusNode.source : null;
    for (const row of codeEl.querySelectorAll('.line')) {
      const ln = Number(row.dataset.line);
      row.classList.toggle('at', ln === line);
      row.classList.toggle('in-range', !!range && ln >= range.line && ln <= (range.endLine || range.line) && ln !== line);
    }
    const at = codeEl.querySelector('.line.at');
    if (at) at.scrollIntoView({ block: 'center', behavior: state.reducedMotion ? 'auto' : 'smooth' });
  }

  return { setGraph, render };
}

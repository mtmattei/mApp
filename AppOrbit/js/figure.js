// Mounts a Hairline figure (figures/*.js, registered through window.hairline) into an element,
// the way the skill's bench does: a stage, an svg, a read-out, and the figure's own number.

const registry = [];
window.hairline = (figure) => registry.push(figure);

const LENS_OF = { ui: 'structure', states: 'states', behavior: 'behavior', routes: 'navigation' };

/** Mounts the named figure. Returns { destroy }. onPick(lens) fires on a click while the read-out names a layer. */
export function mountFigure(host, name, { intensity = 0.5, onPick } = {}) {
  const figure = registry.find((f) => f.name === name);
  if (!figure || !window.HL) return { destroy() {} };
  const HL = window.HL;
  HL.inject(document);
  host.innerHTML = '';
  const stage = document.createElement('div');
  stage.setAttribute('data-hairline', figure.name);
  stage.setAttribute('role', 'img');
  stage.setAttribute('aria-label', figure.means);
  host.appendChild(stage);
  const svg = HL.mk('svg', { viewBox: '0 0 400 320', 'aria-hidden': 'true' }, stage);
  const caption = document.createElement('div');
  caption.className = 'figure-read mono';
  host.appendChild(caption);
  let text = '';
  const read = {
    get textContent() { return text; },
    set textContent(v) { text = v == null ? '' : String(v); caption.textContent = text; },
  };
  const [lo, mid, hi] = figure.range;
  const at = (i) => (i <= 0.5 ? lo + (i / 0.5) * (mid - lo) : mid + ((i - 0.5) / 0.5) * (hi - mid));
  const handle = figure.mount({ stage, svg, read }, at(intensity));
  if (!text) read.textContent = 'rest';
  const onClick = () => {
    const layer = Object.keys(LENS_OF).find((k) => text.includes(`· ${k}`));
    if (layer && onPick) onPick(LENS_OF[layer]);
  };
  stage.addEventListener('click', onClick);
  return {
    destroy() { stage.removeEventListener('click', onClick); handle.destroy(); host.innerHTML = ''; },
  };
}

// One icon set for entity types: 12px grid, one stroke weight, currentColor.

const PATHS = {
  feature: '<rect x="1.5" y="1.5" width="9" height="9" rx="1.5"/><path d="M1.5 4.5h9"/>',
  screen: '<rect x="2.5" y="1" width="7" height="10" rx="1.5"/><path d="M5 9.5h2"/>',
  component: '<rect x="1.5" y="2.5" width="9" height="7" rx="1"/><path d="M4.5 2.5v7"/>',
  'component-instance': '<rect x="1.5" y="2.5" width="9" height="7" rx="1" stroke-dasharray="2 1.4"/><path d="M4.5 2.5v7"/>',
  viewmodel: '<path d="M6 1.3l4.7 4.7L6 10.7 1.3 6z"/>',
  property: '<circle cx="6" cy="6" r="3.3"/>',
  command: '<path d="M3.6 1.6v8.8l6.2-4.4z"/>',
  state: '<circle cx="6" cy="6" r="4.4"/><path d="M6 1.6v8.8A4.4 4.4 0 0 0 6 1.6z" fill="currentColor" stroke="none"/>',
  route: '<path d="M1.5 6h8.2M7 3.3L9.8 6 7 8.7"/>',
  app: '<circle cx="6" cy="6" r="2.2"/><ellipse cx="6" cy="6" rx="5" ry="2" transform="rotate(-24 6 6)"/>',
};

export function iconSvg(type) {
  const body = PATHS[type] || PATHS.property;
  return `<svg class="ico" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.25" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`;
}

/** <span class="glyph {type}"> holding the icon. */
export function glyph(type, extraClass = '') {
  const span = document.createElement('span');
  span.className = `glyph ${type}${extraClass ? ' ' + extraClass : ''}`;
  span.innerHTML = iconSvg(type);
  return span;
}

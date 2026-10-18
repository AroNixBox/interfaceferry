// InterfaceFerry: one way, Figma -> Unity. Every visible layer of the selection becomes a PNG, text stays
// text, and the tree travels with its absolute transforms so Unity can place each one exactly where Figma draws it.
// Transport and pairing are the UI Toolkit Connector's (see Assets/Reference), the payload is our own.

const SCHEMA = 2;
// Longest side of one export in pixels; Unity imports up to 16384, Figma gets slow well before that.
const MAX_EXPORT_PX = 8192;
// Where temporary clones are rendered, far away from anything the user is looking at.
const SCRATCH = -100000;

figma.showUI(__html__, { width: 300, height: 400, themeColors: true });
// Hidden instance children are never exported, so there is no point in walking them.
figma.skipInvisibleInstanceChildren = true;

function postSelection() {
  const sel = figma.currentPage.selection;
  figma.ui.postMessage({ type: 'selection', count: sel.length, names: sel.map((n) => n.name) });
}
figma.on('selectionchange', postSelection);

function hasChildren(node) {
  return 'children' in node && node.children.length > 0;
}

// A mask clips its later siblings, which separate PNGs cannot reproduce, so its container is one picture.
// A boolean operation is one shape no matter how many operands it has.
function isLeaf(node) {
  if (!hasChildren(node)) return true;
  if (node.type === 'BOOLEAN_OPERATION') return true;
  return node.children.some((c) => c.isMask === true);
}

function anyVisible(paints) {
  return Array.isArray(paints) && paints.some((p) => p && p.visible !== false && p.opacity !== 0);
}

// Whether a container draws anything of its own, under its children. A GROUP never does.
function hasOwnVisuals(node) {
  if (node.type === 'GROUP') return false;
  if (anyVisible(node.fills)) return true;
  if (anyVisible(node.strokes) && (node.strokeWeight === figma.mixed || node.strokeWeight > 0)) return true;
  return Array.isArray(node.effects) && node.effects.some((e) => e && e.visible !== false);
}

function isIdentity(t) {
  const e = 1e-6;
  return Math.abs(t[0][0] - 1) < e && Math.abs(t[0][1]) < e && Math.abs(t[1][0]) < e && Math.abs(t[1][1] - 1) < e;
}

function flatTransform(t) {
  return [t[0][0], t[0][1], t[0][2], t[1][0], t[1][1], t[1][2]];
}

function exportScale(bounds, scale) {
  const longest = Math.max(bounds.width, bounds.height, 1);
  return Math.max(0.01, Math.min(scale, MAX_EXPORT_PX / longest));
}

// Renders a node unrotated, so its rotation reaches Unity as a transform rather than baked into pixels.
// 'leaf' renders the node whole, with its own opacity. 'own' renders only what a container draws itself:
// children hidden, opacity 1, because Unity applies a container's opacity to its children as Figma does.
// Direct export when nothing has to change; otherwise a clone on the page, rendered and removed.
async function exportNode(node, mode, scale) {
  const direct = mode === 'leaf' && isIdentity(node.absoluteTransform);
  let target = node;
  let temp = null;
  try {
    if (!direct) {
      temp = node.clone();
      figma.currentPage.appendChild(temp);
      temp.relativeTransform = [[1, 0, SCRATCH], [0, 1, SCRATCH]];
      if (mode === 'own') {
        temp.opacity = 1;
        // Hiding the children would shrink a hug-contents auto layout frame; without layout it keeps its size.
        if ('layoutMode' in temp && temp.layoutMode !== 'NONE') { try { temp.layoutMode = 'NONE'; } catch (e) { } }
        for (const child of temp.children) { try { child.visible = false; } catch (e) { } }
        if (Math.abs(temp.width - node.width) > 0.01 || Math.abs(temp.height - node.height) > 0.01) {
          try { temp.resize(Math.max(node.width, 0.01), Math.max(node.height, 0.01)); } catch (e) { }
        }
      }
      target = temp;
    }

    // Render bounds, not the box: shadows, outside strokes and glyph overhang reach past it.
    const bounds = target.absoluteRenderBounds;
    if (!bounds || bounds.width <= 0 || bounds.height <= 0) return { empty: true };
    const t = target.absoluteTransform;
    const bytes = await target.exportAsync({
      format: 'PNG',
      constraint: { type: 'SCALE', value: exportScale(bounds, scale) }
    });
    return { bytes, x: bounds.x - t[0][2], y: bounds.y - t[1][2], w: bounds.width, h: bounds.height };
  } catch (e) {
    return { error: (e && e.message) || String(e) };
  } finally {
    if (temp) { try { temp.remove(); } catch (e) { } }
  }
}

// Same key scheme as the connector: identical artwork shares one file.
function imageKeyFor(bytes) {
  let h1 = 0x811c9dc5, h2 = 0x01000193;
  for (let i = 0; i < bytes.length; ++i) {
    h1 = Math.imul(h1 ^ bytes[i], 0x01000193);
    h2 = Math.imul(h2 + bytes[i], 0x85ebca6b) ^ (h2 >>> 13);
  }
  return 'img-' + ((h1 >>> 0).toString(36)) + ((h2 >>> 0).toString(36)) + '-' + bytes.length;
}

function stageImage(bytes, ctx) {
  const key = imageKeyFor(bytes);
  if (!ctx.imagesByKey.has(key)) {
    ctx.imagesByKey.add(key);
    ctx.images.push({ imageKey: key, bytes });
  }
  return key;
}

// A property that differs across the text is figma.mixed; the first character's value stands for all of it.
function firstOf(node, prop, rangeGetter) {
  const v = node[prop];
  if (v !== figma.mixed) return { value: v, mixed: false };
  try { return { value: node[rangeGetter](0, 1), mixed: true }; } catch (e) { return { value: null, mixed: true }; }
}

function applyTextCase(s, textCase) {
  if (textCase === 'UPPER') return s.toUpperCase();
  if (textCase === 'LOWER') return s.toLowerCase();
  if (textCase === 'TITLE') return s.replace(/(^|\s)(\S)/g, (m, sep, c) => sep + c.toUpperCase());
  return s;
}

// USS has no line-height and no text-transform, so both travel resolved: pixels, and the characters as shown.
function toPx(spacing, fontSize) {
  if (!spacing || spacing.unit === 'AUTO') return 0;
  return spacing.unit === 'PERCENT' ? spacing.value * fontSize / 100 : spacing.value;
}

// Height of the lines themselves, for a box of fixed height: Unity has to know where they sit inside it.
// Measured on a clone set to grow with its text, which needs the fonts loaded to touch it at all.
async function contentHeight(node) {
  if (node.textAutoResize !== 'NONE' && node.textAutoResize !== 'TRUNCATE') return node.height;
  if (node.textAlignVertical === 'CENTER') return node.height;
  let temp = null;
  try {
    for (const f of node.getRangeAllFontNames(0, node.characters.length)) await figma.loadFontAsync(f);
    temp = node.clone();
    figma.currentPage.appendChild(temp);
    temp.relativeTransform = [[1, 0, SCRATCH], [0, 1, SCRATCH]];
    if ('textTruncation' in temp) temp.textTruncation = 'DISABLED';
    temp.textAutoResize = 'HEIGHT';
    return temp.height;
  } catch (e) {
    return null;
  } finally {
    if (temp) { try { temp.remove(); } catch (e) { } }
  }
}

// Text that USS can draw travels as text; null when it cannot (gradient or image fill), so it stays a PNG.
async function textInfo(node, ctx) {
  const fills = firstOf(node, 'fills', 'getRangeFills');
  const visible = (fills.value || []).filter((p) => p && p.visible !== false && p.opacity !== 0);
  if (visible.length > 1 || (visible.length === 1 && visible[0].type !== 'SOLID')) return null;

  const font = firstOf(node, 'fontName', 'getRangeFontName');
  const size = firstOf(node, 'fontSize', 'getRangeFontSize');
  const weight = firstOf(node, 'fontWeight', 'getRangeFontWeight');
  const letter = firstOf(node, 'letterSpacing', 'getRangeLetterSpacing');
  const line = firstOf(node, 'lineHeight', 'getRangeLineHeight');
  const decoration = firstOf(node, 'textDecoration', 'getRangeTextDecoration');
  const textCase = firstOf(node, 'textCase', 'getRangeTextCase');
  if ([fills, font, size, weight, letter, line, decoration, textCase].some((p) => p.mixed))
    ctx.warnings.push(node.name + ': mixed text styles, the first character\'s style is used');

  const fontSize = typeof size.value === 'number' ? size.value : 12;
  const paint = visible[0];
  const color = paint
    ? [paint.color.r, paint.color.g, paint.color.b, paint.opacity === undefined ? 1 : paint.opacity]
    : [0, 0, 0, 0];
  const shadow = Array.isArray(node.effects)
    ? node.effects.find((e) => e && e.visible !== false && e.type === 'DROP_SHADOW') : null;

  // Figma's soft line break (shift+enter) is U+2028; Unity only breaks on \n.
  const characters = applyTextCase(node.characters, textCase.value).replace(/\u2028/g, '\n');
  return {
    characters,
    family: font.value ? font.value.family : '',
    style: font.value ? font.value.style : '',
    weight: typeof weight.value === 'number' ? weight.value : 400,
    size: fontSize,
    color,
    align: node.textAlignHorizontal,
    valign: node.textAlignVertical,
    autoResize: node.textAutoResize,
    truncate: node.textTruncation === 'ENDING',
    maxLines: typeof node.maxLines === 'number' ? node.maxLines : 0,
    letterSpacing: toPx(letter.value, fontSize),
    lineHeight: toPx(line.value, fontSize),
    paragraphSpacing: typeof node.paragraphSpacing === 'number' ? node.paragraphSpacing : 0,
    decoration: decoration.value || 'NONE',
    contentHeight: await contentHeight(node),
    ...(shadow ? { shadow: { x: shadow.offset.x, y: shadow.offset.y, blur: shadow.radius,
      color: [shadow.color.r, shadow.color.g, shadow.color.b, shadow.color.a] } } : {})
  };
}

async function walk(node, parentIndex, out, ctx) {
  if (node.visible === false) return;

  const leaf = isLeaf(node);
  const entry = {
    id: node.id,
    name: node.name,
    type: node.type,
    parentIndex,
    width: node.width,
    height: node.height,
    t: flatTransform(node.absoluteTransform)
  };
  if (!leaf) {
    entry.opacity = typeof node.opacity === 'number' ? node.opacity : 1;
    entry.clip = node.clipsContent === true;
  }

  // A text's opacity is not baked into pixels here, so it travels like a container's.
  const text = node.type === 'TEXT' ? await textInfo(node, ctx) : null;
  if (text) {
    entry.text = text;
    entry.opacity = typeof node.opacity === 'number' ? node.opacity : 1;
  }

  const mode = text ? null : leaf ? (node.opacity === 0 ? null : 'leaf') : (hasOwnVisuals(node) ? 'own' : null);
  if (mode) {
    ctx.done++;
    figma.ui.postMessage({ type: 'progress', message: 'Rendering ' + ctx.done + ' - ' + node.name });
    const r = await exportNode(node, mode, ctx.scale);
    if (r.bytes) entry.image = { key: stageImage(r.bytes, ctx), x: r.x, y: r.y, w: r.w, h: r.h };
    else if (r.error) ctx.warnings.push(node.name + ': ' + r.error);
  }

  const index = out.length;
  out.push(entry);
  if (!leaf) for (const child of node.children) await walk(child, index, out, ctx);
}

figma.ui.onmessage = async (msg) => {
  if (msg.type === 'get-selection') { postSelection(); return; }

  if (msg.type === 'get-prefs') {
    let token = '', scale = 2;
    try {
      token = (await figma.clientStorage.getAsync('interfaceferry-token')) || '';
      scale = (await figma.clientStorage.getAsync('interfaceferry-scale')) || 2;
    } catch (e) { }
    figma.ui.postMessage({ type: 'prefs', token, scale });
    return;
  }

  if (msg.type === 'save-prefs') {
    try {
      if (typeof msg.token === 'string') await figma.clientStorage.setAsync('interfaceferry-token', msg.token);
      if (typeof msg.scale === 'number') await figma.clientStorage.setAsync('interfaceferry-scale', msg.scale);
    } catch (e) { }
    return;
  }

  if (msg.type === 'collect') {
    const scale = Number(msg.scale) > 0 ? Number(msg.scale) : 2;
    const items = [];
    const warnings = [];
    for (const root of figma.currentPage.selection) {
      const nodes = [];
      const ctx = { scale, images: [], imagesByKey: new Set(), warnings, done: 0 };
      await walk(root, -1, nodes, ctx);
      if (nodes.length > 0)
        items.push({ payload: { schema: SCHEMA, fileName: figma.root.name, scale, nodes }, count: nodes.length, images: ctx.images });
    }
    figma.ui.postMessage({ type: 'collected', items, warnings });
  }
};

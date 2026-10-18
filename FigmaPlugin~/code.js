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

// How far a node's own pixels reach past its box. Figma's render bounds are already cut by the clipping
// parent, so they cannot show what a clip removes; the effects themselves can.
function effectReach(node) {
  const reach = { l: 0, t: 0, r: 0, b: 0 };
  for (const e of (Array.isArray(node.effects) ? node.effects : [])) {
    if (!e || e.visible === false) continue;
    if (e.type === 'LAYER_BLUR') {
      reach.l = Math.max(reach.l, e.radius); reach.t = Math.max(reach.t, e.radius);
      reach.r = Math.max(reach.r, e.radius); reach.b = Math.max(reach.b, e.radius);
    } else if (e.type === 'DROP_SHADOW') {
      const s = (e.radius || 0) + (e.spread || 0);
      reach.l = Math.max(reach.l, s - e.offset.x); reach.r = Math.max(reach.r, s + e.offset.x);
      reach.t = Math.max(reach.t, s - e.offset.y); reach.b = Math.max(reach.b, s + e.offset.y);
    }
  }
  return reach;
}

function hasVisibleText(node) {
  if (node.visible === false) return false;
  if (node.type === 'TEXT') return true;
  return 'children' in node && node.children.some(hasVisibleText);
}

// The radii of a node's own outline per corner (top-left, top-right, bottom-right, bottom-left), as [rx, ry],
// so a circle near a rounded corner is not taken for the square around it.
function outlineRadii(node) {
  if (node.type === 'ELLIPSE') {
    const r = [node.width / 2, node.height / 2];
    return [r, r, r, r];
  }
  if ('topLeftRadius' in node)
    return [node.topLeftRadius, node.topRightRadius, node.bottomRightRadius, node.bottomLeftRadius].map((r) => [r, r]);
  return [[0, 0], [0, 0], [0, 0], [0, 0]];
}

// Whether a clip cuts a node's own pixels: they reach past the clip's edge, or into a rounded corner of it.
// Measured on the box grown by the node's effects.
function isCutBy(node, clipNode) {
  const box = node.absoluteBoundingBox;
  const clip = clipNode.absoluteBoundingBox;
  if (!box || !clip) return false;
  const tol = 0.5;
  const e = effectReach(node);
  const l = box.x - e.l, t = box.y - e.t, r = box.x + box.width + e.r, b = box.y + box.height + e.b;
  if (l < clip.x - tol || t < clip.y - tol || r > clip.x + clip.width + tol || b > clip.y + clip.height + tol) return true;
  if (!('topLeftRadius' in clipNode)) return false;

  const clipRadii = [clipNode.topLeftRadius, clipNode.topRightRadius, clipNode.bottomRightRadius, clipNode.bottomLeftRadius];
  const grow = Math.max(e.l, e.t, e.r, e.b);
  const own = outlineRadii(node);
  const dirs = [[-1, -1], [1, -1], [1, 1], [-1, 1]];
  for (let k = 0; k < 4; k++) {
    const R = Math.min(clipRadii[k], clip.width / 2, clip.height / 2);
    if (!(R > 0)) continue;
    const [dx, dy] = dirs[k];
    // The point of the node's outline furthest into this corner: on its own corner arc, at 45 degrees.
    const rx = Math.min(own[k][0] + grow, (r - l) / 2), ry = Math.min(own[k][1] + grow, (b - t) / 2);
    const px = (dx < 0 ? l : r) - dx * rx * (1 - Math.SQRT1_2);
    const py = (dy < 0 ? t : b) - dy * ry * (1 - Math.SQRT1_2);
    const cx = (dx < 0 ? clip.x : clip.x + clip.width) - dx * R;
    const cy = (dy < 0 ? clip.y : clip.y + clip.height) - dy * R;
    if ((px - cx) * dx > 0 && (py - cy) * dy > 0 && Math.hypot(px - cx, py - cy) > R + tol) return true;
  }
  return false;
}

// Whether anything drawn inside a child is cut by the container's clip. Wrappers that draw nothing are
// looked through, and a nested clip ends the search: it cuts what is inside it before this one could.
function cutsChild(container, child) {
  const visit = (node) => {
    if (node.visible === false) return false;
    if (drawsItself(node) && isCutBy(node, container)) return true;
    if (node.clipsContent === true || !('children' in node)) return false;
    return node.children.some(visit);
  };
  return visit(child);
}

function drawsItself(node) {
  return !hasChildren(node) || node.type === 'BOOLEAN_OPERATION' || hasOwnVisuals(node);
}

// How many of a clipping container's children, from the bottom, are exported into its own picture.
// A child exported on its own is not cut at all, so whatever the clip cuts goes into the container's PNG,
// where Figma cuts it, together with everything beneath it so the stacking stays right. Children above
// the topmost cut one stay separate elements. Text stays text: a cut slice holding text is not merged.
function cutSlice(node, ctx) {
  if (node.clipsContent !== true || !hasChildren(node)) return 0;
  let top = -1;
  node.children.forEach((child, i) => { if (cutsChild(node, child)) top = i; });
  if (top < 0) return 0;
  if (node.children.slice(0, top + 1).some(hasVisibleText)) {
    ctx.warnings.push(node.name + ': its clip cuts a layer, but text lies at or below it, so nothing is merged and Unity cuts to the box');
    return 0;
  }
  ctx.flattened++;
  return top + 1;
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
// 'leaf' renders the node whole, with its own opacity. 'own' renders what a container draws itself plus its
// bottom `keep` children (the cut slice): the rest hidden, opacity 1, because Unity applies a container's
// opacity to its children as Figma does.
// Direct export when nothing has to change; otherwise a clone on the page, rendered and removed.
async function exportNode(node, mode, scale, keep) {
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
        temp.children.forEach((child, i) => { if (i >= (keep || 0)) { try { child.visible = false; } catch (e) { } } });
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

  const slice = isLeaf(node) ? 0 : cutSlice(node, ctx);
  const leaf = isLeaf(node) || slice === node.children.length;
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

  const mode = text ? null : leaf ? (node.opacity === 0 ? null : 'leaf') : (slice > 0 || hasOwnVisuals(node) ? 'own' : null);
  if (mode) {
    ctx.done++;
    figma.ui.postMessage({ type: 'progress', message: 'Rendering ' + ctx.done + ' - ' + node.name });
    const r = await exportNode(node, mode, ctx.scale, slice);
    if (r.bytes) entry.image = { key: stageImage(r.bytes, ctx), x: r.x, y: r.y, w: r.w, h: r.h };
    else if (r.error) ctx.warnings.push(node.name + ': ' + r.error);
  }

  const index = out.length;
  out.push(entry);
  // The cut slice is already in the container's picture; only the children above it are elements.
  if (!leaf) for (const child of node.children.slice(slice)) await walk(child, index, out, ctx);
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
      const ctx = { scale, images: [], imagesByKey: new Set(), warnings, done: 0, flattened: 0 };
      await walk(root, -1, nodes, ctx);
      if (nodes.length > 0)
        items.push({ payload: { schema: SCHEMA, fileName: figma.root.name, scale, nodes }, count: nodes.length, images: ctx.images, flattened: ctx.flattened });
    }
    figma.ui.postMessage({ type: 'collected', items, warnings });
  }
};

// InterfaceFerry: one way, Figma -> Unity. Every visible layer of the selection becomes a PNG, and the
// tree travels with its absolute transforms so Unity can place each PNG exactly where Figma draws it.
// Transport and pairing are the UI Toolkit Connector's (see Assets/Reference), the payload is our own.

const SCHEMA = 1;
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

  const mode = leaf ? (node.opacity === 0 ? null : 'leaf') : (hasOwnVisuals(node) ? 'own' : null);
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

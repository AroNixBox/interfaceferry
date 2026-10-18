# InterfaceFerry

One click, one way: Figma selection → Unity UI Toolkit.

Every visible layer becomes a PNG (export scale 0.5x–4x). Unity rebuilds the same hierarchy as UXML and places each element absolutely from Figma's transforms, so positions, rotations and flips match exactly. Auto layout, constraints and the like are not translated; their effect arrives already resolved as `left`/`top`/`width`/`height`/`rotate`/`scale` in a generated USS.

## Requirements
- Unity 6000.3+
- `com.unity.pipeline` running, with **Allow Browser Clients** on (Window ▸ Pipeline ▸ Settings)
- Figma desktop or web, Design mode

## Install
`Packages/manifest.json`:
```json
"com.aroboss.interfaceferry": "https://github.com/<you>/interfaceferry.git#v0.1.0"
```

## Use
1. Figma: Plugins ▸ Development ▸ Import plugin from manifest… → `FigmaPlugin~/manifest.json` of this repo
   (in Unity: Tools ▸ InterfaceFerry ▸ Reveal Figma Plugin).
2. Unity: Tools ▸ InterfaceFerry ▸ Copy Pipeline Token, paste it in the plugin, press Connect.
   The token changes when the Editor restarts.
3. Pick the export scale, select layers, press **Ferry selection**.

Output: `Assets/InterfaceFerry/<Figma file>/<Screen>.raster.uxml` + `.raster.uss`, PNGs in `raster/<Screen>/`.
Re-pushing a screen overwrites it and removes PNGs it no longer uses.

## How layers become images
- Leaves (text, vectors, shapes, boolean operations, containers holding a mask) render whole, own opacity baked in.
- Containers render only what they draw themselves (fill, stroke, effects), children hidden; their opacity goes to USS so it applies to children as in Figma. Clip content becomes `overflow: hidden`.
- Rotated or flipped layers are exported unrotated through a temporary clone, so the rotation is a USS transform and not baked into pixels.
- Shadows and outside strokes that reach past the box get their own `__raster` child at the render bounds.

## Limits
- Design mode only (exports through temporary clones).
- A container that clips content and also has a shadow clips its own shadow in Unity.
- Blend modes are not carried over; hidden layers are skipped.
- Two roots with the same name in one Figma file overwrite each other.

## Wire protocol (schema 1)
`POST http://localhost:<port>/api/exec`, `Authorization: Bearer <token>`, body `{ "command", "parameters" }`.
- `ferry_status` → `{ interfaceFerry, schemas, project }`
- `ferry_stage_image { image_key, png_base64 }` (PNG only, ≤16 MB)
- `ferry_import { payload: { schema, fileName, scale, nodes[] } }`, node: `id, name, type, parentIndex, width, height, t` (Figma `absoluteTransform`, row-major 6 floats), `opacity`, `clip`, `image { key, x, y, w, h }`.

Bump `SCHEMA` in `code.js` and `Schema` in `FerryCommands.cs` together.

# InterfaceFerry

One click, one way: Figma selection → Unity UI Toolkit.

Every visible layer becomes a PNG (export scale 0.5x–4x), text stays text with its Figma font mapped to a project font file. Unity rebuilds the same hierarchy as UXML and places each element absolutely from Figma's transforms, so positions, rotations and flips match exactly. Auto layout, constraints and the like are not translated; their effect arrives already resolved as `left`/`top`/`width`/`height`/`rotate`/`scale` in a generated USS.

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

Output: `Assets/InterfaceFerry/<Figma file>/<Screen>.raster.uxml` + `.raster.uss`, PNGs in `raster/<Screen>/`, fonts in `<Figma file>.fonts.asset`.
Re-pushing a screen overwrites it and removes PNGs it no longer uses.

## Text and fonts
- TEXT layers arrive as `ui:Label` with characters (text case applied), size, color, alignment, letter spacing, line height, paragraph spacing, wrapping/truncation, underline/strikethrough and a drop shadow as `text-shadow`.
- Fonts come from `<File>.fonts.asset`, next to the screens in `Assets/InterfaceFerry/<Figma file>/`. One row per font: left the font exactly as Figma asks for it (`Hanken Grotesk / Semi Bold`), right a `.ttf`/`.otf` from the project. Every import adds the fonts it needs, keeps assignments, and prefills a row when a file name matches exactly (`HankenGrotesk-SemiBold.ttf`). Use static font files: UI Toolkit draws only the default instance of a variable font.
- Changing a font in that asset rebuilds the file's screens from their last push (kept in `Library/InterfaceFerry/payloads`), no push from Figma needed. An unmapped font falls back to Unity's default font with synthesised bold/italic.
- USS has no line height, so it travels as a `<line-height>` rich text tag; the characters sit in `<noparse>`. Vertical placement matches Figma by centring the lines on a band as tall as Figma's lines, measured in the plugin for fixed-height boxes.
- Text with a gradient or image fill stays a PNG. Mixed styles inside one text use the first character's style (the plugin warns).

## How layers become images
- Leaves (vectors, shapes, boolean operations, containers holding a mask) render whole, own opacity baked in.
- Containers render only what they draw themselves (fill, stroke, effects), children hidden; their opacity goes to USS so it applies to children as in Figma. Clip content becomes `overflow: hidden`.
- Rotated or flipped layers are exported unrotated through a temporary clone, so the rotation is a USS transform and not baked into pixels.
- Shadows and outside strokes that reach past the box get their own `__raster` child at the render bounds.

## Limits
- Design mode only (exports through temporary clones).
- A container that clips content and also has a shadow clips its own shadow in Unity.
- Blend modes are not carried over; hidden layers are skipped.
- Two roots with the same name in one Figma file overwrite each other.

## Known issues
- **Display P3 files shift colors.** In a Figma file set to Display P3 (File ▸ Color profile), PNGs export in P3 (`exportAsync` defaults to `colorProfile: 'DOCUMENT'`) and text colors are sent as raw P3 values; Unity reads both as sRGB, so colors look duller than in Figma. sRGB and legacy files are unaffected. Fix: export with `colorProfile: 'SRGB'` and convert text/shadow colors P3 → sRGB when `figma.root.documentColorProfile === 'DISPLAY_P3'`.
- **Pushing edits the document.** Rotated layers, containers with their own fill/stroke/effects and fixed-height top/bottom-aligned text are exported through a temporary clone on the page (created and removed per layer). Originals and layout are never touched, but the clones are real multiplayer edits: the file counts as edited, and the pusher's undo stack gets a net-empty plugin step. Other users' undo history is unaffected.

## Wire protocol (schema 2)
`POST http://localhost:<port>/api/exec`, `Authorization: Bearer <token>`, body `{ "command", "parameters" }`.
- `ferry_status` → `{ interfaceFerry, schemas, project }`
- `ferry_stage_image { image_key, png_base64 }` (PNG only, ≤16 MB)
- `ferry_import { payload: { schema, fileName, scale, nodes[] } }`, node: `id, name, type, parentIndex, width, height, t` (Figma `absoluteTransform`, row-major 6 floats), `opacity`, `clip`, `image { key, x, y, w, h }`, `text { characters, family, style, weight, size, color, align, valign, autoResize, truncate, maxLines, letterSpacing, lineHeight, paragraphSpacing, decoration, contentHeight, shadow }`.

Bump `SCHEMA` in `code.js` and `Schema` in `FerryCommands.cs` together.

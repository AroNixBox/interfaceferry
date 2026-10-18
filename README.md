# InterfaceFerry

One click, one way: Figma selection → Unity UI Toolkit.

Every visible layer becomes a PNG (export scale 0.5x–4x), text stays text with its Figma font mapped to a project font file. Unity rebuilds the same hierarchy as UXML and places each element absolutely from Figma's transforms, so positions, rotations and flips match exactly. Auto layout, constraints and the like are not translated; their effect arrives already resolved as `left`/`top`/`width`/`height`/`rotate`/`scale` in a generated USS.

## Requirements
- Unity 6000.3+
- `com.unity.pipeline` running, with **Allow Browser Clients** on (Window ▸ Pipeline ▸ Settings)
- Figma desktop or web, Design mode

## Install
Drag & Drop Unity Plugin into Project

## Use
1. Figma: Plugins ▸ Development ▸ Import plugin from manifest… → `FigmaPlugin~/manifest.json`.
   (To find it, in Unity navigate to Tools ▸ InterfaceFerry ▸ Reveal Figma Plugin).
2. Unity: Tools ▸ InterfaceFerry ▸ Copy Pipeline Token, paste it in the plugin, press Connect.
   The token changes when the Editor restarts.
3. Pick the export scale, select layers, press **Ferry selection**.

Output: `Assets/InterfaceFerry/<Figma file>/<Screen>.raster.uxml` + `.raster.uss`, PNGs in `Images/`, fonts in `<Figma file>.fonts.asset`.
One selected layer becomes a screen named after it. Several selected layers become **one** screen with them as siblings, in Figma's stacking order and at their positions relative to each other, under a root spanning them all, named after the selected layers joined with ` - ` in stacking order (capped at 100 characters, the rest counted as ` +N`).
Re-pushing a screen overwrites it.

**Images are shared per Figma file.** All screens of one Figma file use one `Images/` folder; a PNG is named by its content hash, so identical artwork exists once. The plugin asks Unity which hashes it already holds and uploads only the new ones; existing PNGs are not touched or re-imported. Cleaning up is its own step, `ferry_cleanup_images`: it deletes the PNGs in `Images/` that no asset in the project refers to any more (by Unity's dependency data: generated and hand-made USS, UXML, prefabs, scenes, materials). The plugin calls it after every push, a rebuild runs it once at the end, and Tools ▸ InterfaceFerry ▸ Clean Up Unused Images runs it for every Figma file. Screens pushed before 0.2.0 keep their old `raster/<Screen>/` folder until they are pushed again, which moves their PNGs into `Images/`; `raster/` is removed once empty.

## Text and fonts
- TEXT layers arrive as `ui:Label` with characters (text case applied), size, color, alignment, letter spacing, line height, paragraph spacing, wrapping/truncation, underline/strikethrough and a drop shadow as `text-shadow`.
- Fonts come from `<File>.fonts.asset`, next to the screens in `Assets/InterfaceFerry/<Figma file>/`. One row per font: left the font exactly as Figma asks for it (`Hanken Grotesk / Semi Bold`), right a `.ttf`/`.otf` from the project. Every import adds the fonts it needs, keeps assignments, and prefills a row when a file name matches exactly (`HankenGrotesk-SemiBold.ttf`). Use static font files: UI Toolkit draws only the default instance of a variable font.
- Changing a font in that asset rebuilds the file's screens from their last push (kept in `Library/InterfaceFerry/payloads`), no push from Figma needed. An unmapped font falls back to Unity's default font with synthesised bold/italic.
- USS has no line height, so it travels as a `<line-height>` rich text tag; the characters sit in `<noparse>`. Vertical placement matches Figma by centring the lines on a band as tall as Figma's lines, measured in the plugin for fixed-height boxes.
- Text with a gradient or image fill stays a PNG. Mixed styles inside one text use the first character's style (the plugin warns).

## How layers become images
- **Whatever a clip cuts is exported with its parent.** A layer exported on its own is not cut at all, so when a clipping container cuts a child (past its edge or into a rounded corner, including the reach of a layer blur or drop shadow), that child goes into the container's PNG, where Figma cuts it, together with every sibling beneath it so the stacking stays right. Siblings above the topmost cut child stay separate elements; if the topmost child is cut, the container becomes one PNG. This applies to the pushed root too. Cuts are measured on the real outline (a circle near a rounded corner is not cut), wrappers that draw nothing are looked through, and a nested clip decides for what is inside it.
- Leaves (vectors, shapes, boolean operations, containers holding a mask) render whole, own opacity baked in.
- Containers render only what they draw themselves (fill, stroke, effects), children hidden; their opacity goes to USS so it applies to children as in Figma. Clip content becomes `overflow: hidden`.
- Rotated or flipped layers are exported unrotated through a temporary clone, so the rotation is a USS transform and not baked into pixels.
- Shadows and outside strokes that reach past the box get their own `__raster` child at the render bounds.

## Limits
- Design mode only (exports through temporary clones).
- A container that clips content and also has a shadow clips its own shadow in Unity.
- Blend modes are not carried over; hidden layers are skipped.
- **Background blur** (frosted glass) is lost: it blurs what lies behind the layer, usually a sibling, which no PNG of the layer itself contains (SVG does not help, Unity's SVG importer has no filters). Options: bake the backdrop into the export (static backgrounds only), or USS `backdrop-filter`, which needs Unity 6000.6+.
- If text lies at or below a cut layer, nothing is merged (text stays text): Unity then cuts to the square box, and the plugin warns.
- Cut detection uses each layer's box, effects and corner radii, not its exact vector outline; an irregular shape inside a corner may be merged although Figma cuts nothing from it.
- Two roots with the same name in one Figma file overwrite each other.

## Known issues
- **Display P3 files shift colors.** In a Figma file set to Display P3 (File ▸ Color profile), PNGs export in P3 (`exportAsync` defaults to `colorProfile: 'DOCUMENT'`) and text colors are sent as raw P3 values; Unity reads both as sRGB, so colors look duller than in Figma. sRGB and legacy files are unaffected. Fix: export with `colorProfile: 'SRGB'` and convert text/shadow colors P3 → sRGB when `figma.root.documentColorProfile === 'DISPLAY_P3'`.
- **Pushing edits the document.** Rotated layers, containers with their own fill/stroke/effects and fixed-height top/bottom-aligned text are exported through a temporary clone on the page (created and removed per layer). Originals and layout are never touched, but the clones are real multiplayer edits: the file counts as edited, and the pusher's undo stack gets a net-empty plugin step. Other users' undo history is unaffected.

## Future improvements
- **Drop `__raster` for leaves.** When a layer's PNG reaches past its box (outside strokes, shadows, blur), the image sits on a `__raster` child sized to the render bounds, because `background-image` always fills the element's own box. A layer without children does not need Figma's box: its element could take the render bounds itself (offset folded into its position, rotation included) and carry the PNG as its own background. `__raster` would then remain only for containers with children whose own pixels reach past their box.

## Wire protocol (schema 2)
`POST http://localhost:<port>/api/exec`, `Authorization: Bearer <token>`, body `{ "command", "parameters" }`. A push runs `ferry_missing_images` → `ferry_stage_image` per missing PNG → `ferry_import` → `ferry_cleanup_images`; `ferry_status` runs on connect.
- `ferry_status` → `{ interfaceFerry, schemas, project }`
- `ferry_missing_images { file_name, image_keys[] }` → `{ missing[] }`, the keys the file's `Images/` folder does not hold yet
- `ferry_cleanup_images { file_name? }` → `{ removed }`, deletes unreferenced PNGs of that file's `Images/` folder, or of every file's without `file_name`
- `ferry_stage_image { image_key, png_base64 }` (PNG only, ≤16 MB)
- `ferry_import { payload: { schema, fileName, scale, nodes[] } }`, node: `id, name, type, parentIndex, width, height, t` (Figma `absoluteTransform`, row-major 6 floats), `opacity`, `clip`, `image { key, x, y, w, h }`, `text { characters, family, style, weight, size, color, align, valign, autoResize, truncate, maxLines, letterSpacing, lineHeight, paragraphSpacing, decoration, contentHeight, shadow }`.

Bump `SCHEMA` in `code.js` and `Schema` in `FerryCommands.cs` together.

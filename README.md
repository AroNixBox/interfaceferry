# InterfaceFerry

One click, one way: Figma selection → Unity UI Toolkit or uGUI.

Every visible layer becomes a PNG (export scale 0.5x–4x), text stays text with its Figma font mapped to a project font file. Unity rebuilds the hierarchy as UXML or as uGUI GameObjects and places each element from Figma's transforms, preserving positions, rotations and flips. Auto layout, constraints and the like are not translated; their effect arrives already resolved as position, dimensions, rotation and scale, applied through USS or RectTransform.

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
3. Unity: **Project Settings ▸ InterfaceFerry ▸ Receive as** selects **UI Toolkit** (default) or **uGUI**. Tools ▸ InterfaceFerry ▸ Settings opens it. The selection is saved per project.
4. Pick the export scale, select layers, press **Ferry selection**. The plugin checks the receiver before each push and keeps that target for the whole push.

Output under `Assets/InterfaceFerry/<ProjectName>/`:
```text
<ProjectName>/
  UITK/
    Fonts/
    Images/
    ColorPickerHome.uxml
    ColorPickerHome.uss
  UGUI/
    Fonts/
    Images/
    ColorPickerHome.unity
```
Project and generated asset names use ASCII letters/digits with PascalCase word boundaries: `Color Picker / Home` becomes `ColorPickerHome`. Images keep their content hashes. Existing output is moved through the AssetDatabase on the next import, preserving GUIDs; occupied destinations are never overwritten by migration.

UITK images are Default textures; uGUI images are Single sprites with Full Rect meshes, displayed with normal `UnityEngine.UI.Image` components. Each backend has its own font mappings. New mappings initially copy an existing assignment from the other backend; subsequent edits are independent. Generated TMP fonts also live in `UGUI/Fonts/`, with a `TMP` filename suffix.

The uGUI scene has an active orthographic `MainCamera` clearing to black (no scene geometry), a Screen Space Overlay Canvas, a fixed **9584 × 1432** design viewport and no CanvasScaler. The imported root has its rectangle centre at the viewport centre (centre anchors and pivot). Children keep their local Figma coordinates; no responsive layout or automatic scaling is added. Configure the Game View/player to your fixed display resolution separately. Import saves and closes its generated scene, preserving the scene you were editing. Close a generated scene before pushing a replacement for it.

uGUI objects are normal GameObjects: add Button components and Inspector On Click listeners as needed. This release imports the existing visual payload; it does not infer buttons or import Figma prototype interactions.
One selected layer becomes a screen named after it. Several selected layers become **one** screen with them as siblings, in Figma's stacking order and at their positions relative to each other, under a root spanning them all, named after the selected layers joined with ` - ` in stacking order (capped at 100 characters, the rest counted as ` +N`).
Re-pushing a screen overwrites it.

**Images are shared per Figma file and receiver.** Screens share `UITK/Images/` or `UGUI/Images/`; a PNG is named by its content hash, so identical artwork exists once per receiver. When both receivers use an image, Unity copies the same PNG bytes to the other folder without uploading them again. Each copy keeps its own import settings. The plugin asks Unity which hashes it already holds in either receiver or the legacy flat Images folder and uploads only the new ones; existing PNGs are not touched or re-imported. Cleaning up is its own step, `ferry_cleanup_images`: it checks both receiver folders and legacy flat `Images/` and deletes PNGs that no asset in the project refers to any more (by Unity's dependency data: generated and hand-made USS, UXML, prefabs, scenes, materials). The plugin calls it after every push, and Tools ▸ InterfaceFerry ▸ Clean Up Unused Images runs it for every Figma file. Existing flat `Images/` files stay valid for old screens. A new push copies only the required images to the selected receiver folder. Old `raster/<Screen>/` images are also reused; the old screen folder is removed after a successful UITK re-push.

## Text and fonts
- TEXT layers arrive as `ui:Label` with characters (text case applied), size, color, alignment, letter spacing, line height, paragraph spacing, wrapping/truncation, underline/strikethrough and a drop shadow as `text-shadow`.
- Each Figma family and style (`Hanken Grotesk / SemiBold`) has one `FerryFont` ScriptableObject in `Assets/InterfaceFerry/<ProjectName>/<UITK or UGUI>/Fonts/`, shared by that backend's screens. Font assets have names such as `HankenGroteskSemiBold.asset`. Drag the matching static `.ttf` file into the font field. Imports create missing assets and reuse existing assignments within that Figma file; filenames that sanitize to the same name get distinct assets.
- Labels directly bind `style.unityFontDefinition` to that asset's `font` field in UXML (`ToTarget`), without a custom converter. The data source lives in a runtime assembly. Bold/italic are not synthesized over static font cuts.
- USS has no line height, so it travels as a `<line-height>` rich text tag; the characters sit in `<noparse>`. Vertical placement matches Figma by centring the lines on a band as tall as Figma's lines, measured in the plugin for fixed-height boxes.
- Text with a gradient or image fill stays a PNG. Mixed styles inside one text use the first character's style (the plugin warns).

## uGUI text
- Exportable text remains editable `TextMeshProUGUI`; gradient/image-filled text still arrives as a PNG.
- The uGUI backend's `FerryFont` source `.ttf` assignments are used to generate persistent TMP fonts. After changing a source font assignment, re-push the uGUI screen; UITK continues to use its existing live font binding.
- TMP essentials are installed without an interactive import dialog if missing. If Unity is still finishing that first resource import, push again once it completes.
- Text engine metrics can differ from UITK. Shadow offset/color is retained with a separate TMP shadow layer; blurred text shadows are reported as a limitation.

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

## License
InterfaceFerry is MIT-licensed, **except** for a few portions derived from Unity Technologies' `com.unity.ui.figma` (UI Toolkit Connector for Figma design), which remain under the Unity Companion License. They are marked in the source with `Derived from com.unity.ui.figma` and listed file by file in [Third Party Notices.md](Third%20Party%20Notices.md). See [LICENSE.md](LICENSE.md).

## Wire protocol (schema 2)
`POST http://127.0.0.1:<port>/api/exec`, `Authorization: Bearer <token>`, body `{ "command", "parameters" }`. A push runs `ferry_missing_images` → `ferry_stage_image` per missing PNG → `ferry_import` → `ferry_cleanup_images`; `ferry_status` runs on connect.
- `ferry_status` → `{ interfaceFerry, schemas, project, receiveAs }`, receiveAs is `UITK` or `UGUI`
- `ferry_missing_images { file_name, image_keys[], target? }` → `{ missing[] }`, the keys not already available in either receiver folder or legacy `Images/`
- `ferry_cleanup_images { file_name? }` → `{ removed }`, deletes unreferenced PNGs of that file's `Images/` folder, or of every file's without `file_name`
- `ferry_stage_image { image_key, png_base64 }` (PNG only, ≤16 MB)
- `ferry_import { target?, payload: { schema, fileName, scale, nodes[] } }`, node: `id, name, type, parentIndex, width, height, t` (Figma `absoluteTransform`, row-major 6 floats), `opacity`, `clip`, `image { key, x, y, w, h }`, `text { characters, family, style, weight, size, color, align, valign, autoResize, truncate, maxLines, letterSpacing, lineHeight, paragraphSpacing, decoration, contentHeight, shadow }`.

`target` accepts `UITK` or `UGUI`; omitted uses the Unity project setting. The new plugin passes the target explicitly for the duration of a push; schema 2 raster payloads are unchanged. Import results return `uxml`/`uss` for UITK or `scene` for uGUI, plus counts and notes.

Bump `SCHEMA` in `code.js` and `Schema` in `FerryCommands.cs` together.

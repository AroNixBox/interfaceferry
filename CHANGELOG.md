# Changelog

## [Unreleased]

## [0.3.0] - 2026-10-21
- Added a project receiver setting for UI Toolkit or uGUI, reported to the Figma plugin and frozen for each push.
- Output is grouped as `<ProjectName>/<UITK or UGUI>/{Fonts,Images}`, with PascalCase project/scene/style/font filenames. Legacy assets move through the AssetDatabase with GUIDs retained.
- Generated uGUI scenes include an active MainCamera so the Game View has a rendering camera.
- Added a uGUI scene generator with normal Image components, editable TMP text, fixed 9584 × 1432 viewport, centred root rectangle and no CanvasScaler. Imports preserve the active scene.
- Split image assets into UITK/Images (Default textures) and UGUI/Images (Single, Full Rect sprites). Existing flat image assets remain valid; bytes can be reused across receivers without another upload. Cleanup checks references from both receivers.
- Added live receiver checks and plugin transport checks. Existing raster payload schema remains version 2.

- Fonts now use one runtime `FerryFont` ScriptableObject per Figma family/style in `Assets/InterfaceFerry/<ProjectName>/<UITK or UGUI>/Fonts/`, shared by that backend's screens. Asset names use PascalCase, such as `HankenGroteskMedium`. Labels bind directly to the `font` field in UXML.
- Removed `FerryFontMap`, payload JSON persistence, screen rebuilding on font changes and font-change signatures. Existing imports need a new push to create the per-font assets; automatic migration is not included.
- Cleaning up unreferenced PNGs is its own command, `ferry_cleanup_images`, instead of part of `ferry_import`: the plugin calls it after every push, and Tools ▸ InterfaceFerry ▸ Clean Up Unused Images runs it for every Figma file.

## [0.2.0] - 2026-10-18
- Images are shared per Figma file: one `Images/` folder per file instead of `raster/<Screen>/` per screen, PNGs named by content hash. The plugin uploads only PNGs Unity does not hold yet (`ferry_missing_images`); existing ones are not re-imported. After every import, PNGs no asset in the project refers to are deleted. Screens from 0.1.0 move into `Images/` on their next push, and the old `raster/` goes once empty.
- Several selected layers are pushed as one screen with them as siblings (stacking order and relative positions kept) instead of one screen each, named after the selected layers joined with " - ".
- Whatever a clip cuts is exported with its parent: the cut child and everything beneath it go into the clipping container's PNG, children above stay elements; also for the pushed root. Cuts are measured on box, effects and corner radii. The plugin reports how many containers were merged.
- An import folder that exists on disk but is not yet imported is imported instead of duplicated as "<name> 1".

## [0.1.0] - 2026-10-18
- First version: Figma plugin (`FigmaPlugin~/`) pushes the selection as PNGs; `ferry_status`, `ferry_stage_image` and `ferry_import` Pipeline commands build `<Screen>.raster.uxml` + `.uss` under `Assets/InterfaceFerry/<File>/`.
- Text travels as text: TEXT layers become `ui:Label` with size, color, alignment, spacing, line height, truncation and decoration.
- Font map per Figma file (`<File>.fonts.asset` in its import folder): the font as Figma asks for it → a project .ttf/.otf. Filled on every import, prefilled on an exact file name match; a change rebuilds that file's screens.
- Wire schema 2: an older plugin or package is refused instead of importing empty text.
- A text-only screen no longer gets an empty `raster/` folder.

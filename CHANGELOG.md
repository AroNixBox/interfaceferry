# Changelog

## [0.2.0] - 2026-10-18
- Images are shared per Figma file: one `raster/` folder per file instead of one per screen, PNGs named by content hash. The plugin uploads only PNGs Unity does not hold yet (`ferry_missing_images`); existing ones are not re-imported. After every import, PNGs no asset in the project refers to are deleted. Screens from 0.1.0 move into the shared folder on their next push.
- Several selected layers are pushed as one screen with them as siblings (stacking order and relative positions kept) instead of one screen each, named after the selected layers joined with " - ".
- Whatever a clip cuts is exported with its parent: the cut child and everything beneath it go into the clipping container's PNG, children above stay elements; also for the pushed root. Cuts are measured on box, effects and corner radii. The plugin reports how many containers were merged.
- An import folder that exists on disk but is not yet imported is imported instead of duplicated as "<name> 1".

## [0.1.0] - 2026-10-18
- First version: Figma plugin (`FigmaPlugin~/`) pushes the selection as PNGs; `ferry_status`, `ferry_stage_image` and `ferry_import` Pipeline commands build `<Screen>.raster.uxml` + `.uss` under `Assets/InterfaceFerry/<File>/`.
- Text travels as text: TEXT layers become `ui:Label` with size, color, alignment, spacing, line height, truncation and decoration.
- Font map per Figma file (`<File>.fonts.asset` in its import folder): the font as Figma asks for it → a project .ttf/.otf. Filled on every import, prefilled on an exact file name match; a change rebuilds that file's screens.
- Wire schema 2: an older plugin or package is refused instead of importing empty text.
- A text-only screen no longer gets an empty `raster/` folder.

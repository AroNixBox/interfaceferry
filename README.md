# InterfaceFerry

InterfaceFerry consists of a Unity Package and a Figma Plugin. Use it to quickly 

### Important
The Figma Plugin creates temporary copies of all the Figma Nodes, if you work in a shared workspace, copy out the Elements you want to export into a new Figma File.

### Set up

1. Figma: Plugins ▸ Development ▸ Import plugin from manifest… → `FigmaPlugin~/manifest.json`.
   (To find it, in Unity navigate to Tools ▸ InterfaceFerry ▸ Reveal Figma Plugin).
2. Unity: Project Settings ▸ Pipeline ▸ Editor ▸ Check "Enable Browser Clients" to enable communication from figma to Unity, then hit RESTART
3. Unity: Tools ▸ InterfaceFerry ▸ Copy Pipeline Token
4. Figma: Paste the Pipeline Token in the Plugin, press Connect.
   The token changes when the Editor restarts.
5. Pick the export scale, select layers, press **Ferry selection**.

Output under `Assets/InterfaceFerry/`:
```text
<ProjectName>/
  Fonts/
  Images/
  ColorPickerHome.unity
```

### Images:
- The Figma hierarchy is preserved except where children must be baked together for clipping. If a node's graphics extend beyond its bounds (for example, a drop shadow), a `__raster` child holds the image at its exported size and offset. Visual overflow alone does not create a Mask.
- A container with **Clip content** enabled uses a Unity Mask while its children remain separate, with its own image displayed in a `__raster` child. This does not automatically combine all of its children. If all children are baked into one PNG, clipping is already part of that image and no separate Unity Mask is needed.
- If the container cuts a child's visible content, that child and every sibling below it in drawing order are baked into one PNG together with the container's own graphics. This preserves Figma's clipping and stacking order. The baked children are not recreated as individual Unity objects; children above the topmost cut child remain separate.
- If the area that would be combined contains visible text, including text inside nested containers, none of those children are combined. They remain separate, text stays editable, and the container's Unity Mask handles clipping instead.


### Fonts:
- Double import for Fonts:
  - first one creates the Data Containers in /Fonts - they then need to be assigned
  - After assigning, the selected Node must be reimported from figma, only then the correct font is applied.

### Constraints
- Design mode only
- Hidden layers are skipped.
- Display P3 files shift colors, unity imports as rbg
- Rounded corners may not clip correctly when text is involved. Text stays editable, and the plugin shows a warning.
- Background blur is lost
- A container that clips content and also has a shadow clips its own shadow in Unity, because mask cuts the `__raster`

### Known issues
- Centers the Figma root node in the canvas without respecting the size
- Drop shadows baked into exported PNGs show grainy edges and visible artifacts in Unity instead of the smooth falloff shown in Figma.
- Two roots with the same name in one Figma file overwrite each other.

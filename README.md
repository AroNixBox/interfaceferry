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
- The Hierarchy from Figma is transferred 1:1 into Unity. If a Figma node has a visual overflow (any effect that juts out its own size), a `__raster` Element is childed to the Element to keep the original hierarchy untouched.
- If an Parent clips its visual content, both elements are exported as one to prevent a wrong display of the element.


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
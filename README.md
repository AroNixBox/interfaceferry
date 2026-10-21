# InterfaceFerry

InterfaceFerry consists of a Unity Package and a Figma Plugin.

### Important
The Figma Plugin creates temporary copies of all the Figma Nodes, if you work in a shared workspace, copy out the Elements you want to export into a new Figma File.

### Set up

1. Figma: Plugins ▸ Development ▸ Import plugin from manifest… → `FigmaPlugin~/manifest.json`.
   (To find it, in Unity navigate to Tools ▸ InterfaceFerry ▸ Reveal Figma Plugin).
2. Unity: Project Settings ▸ Pipeline ▸ Editor ▸ Check "Enable Browser Clients" to enable communication from figma to Unity
3. Unity: Tools ▸ InterfaceFerry ▸ Copy Pipeline Token
4. Figma: Paste the Pipeline Token in the Plugin, press Connect.
   The token changes when the Editor restarts.
5. Unity: **Project Settings ▸ InterfaceFerry ▸ Receive as** selects **UI Toolkit** or **uGUI**.
6. Pick the export scale, select layers, press **Ferry selection**.

Output under `Assets/InterfaceFerry/:
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

### FYI
- The Hierarchy from Figma is transferred 1:1 into Unity. If a Figma node has a visual overflow (any effect that juts out its own size), a `__raster` Element is childed to the Element to keep the original hierarchy untouched.
- If an Parent clips its visual content, both elements are exported as one to prevent a wrong display of the element.

### Restrictions
- Design mode only (exports through temporary clones).
- Hidden layers are skipped.
- Two roots with the same name in one Figma file overwrite each other.
- Display P3 files shift colors, unity imports as rbg
- Rounded corners may not clip correctly when text is involved. Text stays editable, and the plugin shows a warning.
- Background blur is lost
- A container that clips content and also has a shadow clips its own shadow in Unity, because mask cuts the `__raster`

UGUI:
- Every visual is a Transform with an image
- Centers the Figma root node in the canvas without respecting the size
- Double import for Fonts, first one creates the Data Containers in /Fonts - they then need to be assigned, reimport from figma so they get applied

UITK:
- Every visual is a VisualElement with the Image applied as Texture
- Figma root node is being placed top left corner, the size of the Panel settings is set nowhere
- Vectorpath exports have small border artifacts


## Known issues
- **Pushing edits the document.** Rotated layers, containers with their own fill/stroke/effects and fixed-height top/bottom-aligned text are exported through a temporary clone on the page (created and removed per layer). Originals and layout are never touched, but the clones are real multiplayer edits: the file counts as edited, and the pusher's undo stack gets a net-empty plugin step. Other users' undo history is unaffected.

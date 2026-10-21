# Receiver checks

From the package folder, run the real plugin UI with a mocked transport:

```sh
node Tests~/Receivers/plugin-receiver.test.js
```

With the containing Unity project open, run the asset/scene checks through its Pipeline server:

```sh
unity command run_script --project-path /path/to/project --caller plugin --skill unity-cli --file Packages/com.aroboss.interfaceferry/Tests~/Receivers/ReceiverChecks.cs --entry ReceiverChecks.Run --timeout_ms 30000 --format json
```

These create and remove only `Assets/InterfaceFerry/FerryReceiverChecks` and refuse to overwrite it if it already exists. TMP essentials must be present for the mapped-font conversion check. They exercise real imports, texture settings, editable text and persisted font assets, saved scene dependencies, both image folders, nested transforms, raster bounds, fixed viewport, centred rectangle, scene isolation and cleanup.

The Pipeline envelope may succeed even if the script fails: check `data.result.success` and the returned assertion details.

Migration checks (isolated fixture, removed afterwards):
```sh
unity command run_script --file Packages/com.aroboss.interfaceferry/Tests~/Receivers/MigrationChecks.cs --entry MigrationChecks.Run --timeout_ms 30000 --project-path <project> --caller plugin --skill unity-cli --format json
```
Checks legacy flat images and two screens after migration, retained GUIDs, font binding paths and repeatability.

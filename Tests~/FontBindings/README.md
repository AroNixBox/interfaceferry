# Font binding checks

Run from the Unity project root with .NET 10 and Unity 6000.3.22f1 installed:

```sh
dotnet run --project Packages/com.aroboss.interfaceferry/Tests~/FontBindings/FontBindings.csproj -- --existing-project
```

These checks compile the real package sources against installed Unity assemblies and exercise readable font names, direct font mapping in emitted UXML/USS, XML escaping and font identity. `--existing-project` also checks that existing screens reference files in their own project's `Fonts/` folder without a custom converter, without writing to them.

Override `UnityContents` with `--property:UnityContents=/path/to/Unity.app/Contents` for another installation. Use `--property:BaseIntermediateOutputPath=/tmp/ferry-check/obj/ --property:OutputPath=/tmp/ferry-check/bin/` to keep build artifacts outside the package.

Check that the runtime sources compile without Editor references:

```sh
dotnet build Packages/com.aroboss.interfaceferry/Tests~/FontBindings/FontBindings.csproj --property:VerifyRuntimeOnly=true
```

These are not live Unity tests. AssetDatabase creation/reuse, UI Builder previews and attached-panel updates still require checking in the Editor.

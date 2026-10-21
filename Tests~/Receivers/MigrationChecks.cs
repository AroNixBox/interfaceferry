using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class MigrationChecks
{
    public static string Run()
    {
        const string file = "__Ferry Migration Checks";
        const string old = "Assets/InterfaceFerry/" + file;
        const string folder = "Assets/InterfaceFerry/FerryMigrationChecks";
        if (AssetDatabase.IsValidFolder(old) || AssetDatabase.IsValidFolder(folder)) throw new Exception("Fixture exists");
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "InterfaceFerry.Editor");
        var importer = assembly.GetType("InterfaceFerry.FerryImporter");
        try
        {
            var font = assembly.GetType("InterfaceFerry.FerryFontAssets").GetMethod("LoadOrCreate").Invoke(null,
                new object[] { old + "/Fonts", "Legacy / Regular", AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf") }) as UnityEngine.Object;
            var fontGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(font));
            importer.GetMethod("EnsureFolder", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { old + "/Images" });
            var texture = new Texture2D(2, 2);
            File.WriteAllBytes(old + "/Images/legacy.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(old + "/Images/legacy.png");
            var imageGuid = AssetDatabase.AssetPathToGUID(old + "/Images/legacy.png");
            var uri = (string)assembly.GetType("InterfaceFerry.FerryFontAssets").GetMethod("AssetUri").Invoke(null, new object[] { font });
            File.WriteAllText(old + "/Legacy Screen.raster.uss", ".test { background-image: url(\"Images/legacy.png\"); }");
            File.WriteAllText(old + "/Legacy Screen.raster.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><Style src=\"Legacy Screen.raster.uss\"/><ui:Label><Bindings><ui:DataBinding property=\"style.unityFontDefinition\" data-source=\"" + uri.Replace("&", "&amp;") + "\" data-source-path=\"font\" binding-mode=\"ToTarget\" /></Bindings></ui:Label><ui:VisualElement class=\"test\" /></ui:UXML>");
            File.Copy(old + "/Legacy Screen.raster.uss", old + "/Other Screen.raster.uss");
            File.WriteAllText(old + "/Other Screen.raster.uxml", File.ReadAllText(old + "/Legacy Screen.raster.uxml").Replace("Legacy Screen.raster.uss", "Other Screen.raster.uss"));
            AssetDatabase.Refresh();
            var uxmlGuid = AssetDatabase.AssetPathToGUID(old + "/Legacy Screen.raster.uxml");
            importer.GetMethod("MigrateLayout", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { file });
            Check(AssetDatabase.GUIDToAssetPath(imageGuid) == folder + "/UITK/Images/legacy.png", "Flat image migration changed GUID/path: " + imageGuid + " -> " + AssetDatabase.GUIDToAssetPath(imageGuid));
            Check(AssetDatabase.GUIDToAssetPath(fontGuid) == folder + "/UITK/Fonts/LegacyRegular.asset", "Font migration changed GUID/path");
            var uxml = folder + "/UITK/LegacyScreen.uxml";
            Check(AssetDatabase.AssetPathToGUID(uxml) == uxmlGuid, "UXML GUID changed");
            Check(AssetDatabase.GetDependencies(uxml).Contains(folder + "/UITK/Images/legacy.png"), "Moved UXML lost its image");
            Check(AssetDatabase.GetDependencies(folder + "/UITK/OtherScreen.uxml").Contains(folder + "/UITK/Images/legacy.png"), "Other migrated screen lost shared image");
            Check(File.ReadAllText(uxml).Contains("/UITK/Fonts/LegacyRegular.asset"), "Font binding URI did not follow migration");
            importer.GetMethod("MigrateLayout", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { file });
            Check(AssetDatabase.AssetPathToGUID(uxml) == uxmlGuid, "Migration not idempotent");
            return "PASS: legacy flat images, fonts and styles retain GUIDs and dependencies; migration is idempotent.";
        }
        finally
        {
            if (AssetDatabase.IsValidFolder(old)) AssetDatabase.DeleteAsset(old);
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace InterfaceFerry
{
    static class FerryFontAssets
    {
        internal const string FolderName = "Fonts";

        public static string Required(string family, string style) => (family ?? "").Trim() + " / " + (style ?? "").Trim();

        internal static string AssetName(string figmaName) => FerryImporter.SafeName((figmaName ?? "").Replace(" / ", " - "), "Font");

        public static FerryFont LoadOrCreate(string folder, string figmaName, Font font = null)
        {
            FerryImporter.EnsureFolder(folder);
            foreach (var guid in AssetDatabase.FindAssets("t:FerryFont", new[] { folder }))
            {
                var existing = AssetDatabase.LoadAssetAtPath<FerryFont>(AssetDatabase.GUIDToAssetPath(guid));
                if (existing != null && string.Equals(existing.figmaName, figmaName, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }

            var asset = ScriptableObject.CreateInstance<FerryFont>();
            asset.figmaName = figmaName;
            // Seed a new backend mapping from the other backend; subsequent edits are independent.
            if (font == null && (folder.Contains("/UGUI/") || folder.Contains("/UITK/")))
            {
                var other = folder.Contains("/UGUI/") ? folder.Replace("/UGUI/", "/UITK/") : folder.Replace("/UITK/", "/UGUI/");
                if (AssetDatabase.IsValidFolder(other))
                    foreach (var guid in AssetDatabase.FindAssets("t:FerryFont", new[] { other }))
                    {
                        var source = AssetDatabase.LoadAssetAtPath<FerryFont>(AssetDatabase.GUIDToAssetPath(guid));
                        if (source != null && string.Equals(source.figmaName, figmaName, StringComparison.OrdinalIgnoreCase)) { font = source.font; break; }
                    }
            }
            asset.font = font;
            var path = FerryImporter.UniqueAssetPath(folder + "/" + AssetName(figmaName) + ".asset");
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static string AssetUri(FerryFont asset)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long fileId))
                throw new InvalidOperationException("Font asset must be saved before generating its binding.");
            var path = string.Join("/", AssetDatabase.GetAssetPath(asset).Split('/').Select(Uri.EscapeDataString));
            return "project://database/" + path + "?fileID=" + fileId + "&guid=" + guid + "&type=2#" + Uri.EscapeDataString(asset.name);
        }
    }
}

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
            asset.font = font;
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + AssetName(figmaName) + ".asset");
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

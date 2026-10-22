using System;
using UnityEditor;
using UnityEngine;

namespace InterfaceFerry
{
    static class FerryFontAssets
    {
        public static string Required(string family, string style) => (family ?? "").Trim() + " / " + (style ?? "").Trim();

        static string AssetName(string figmaName) => FerryImporter.SanitizeName((figmaName ?? "").Replace(" / ", " - "));

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
            var path = FerryImporter.UniqueAssetPath(folder + "/" + AssetName(figmaName) + ".asset");
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using InterfaceFerry.Editor.Commands;
using UnityEditor;
using UnityEngine;

namespace InterfaceFerry
{
    /// <summary>
    /// 2D affine transform, row-major like Figma's Transform.
    /// </summary>
    struct Affine
    {
        public double m00, m01, m02, m10, m11, m12;

        public static Affine From(float[] t)
        {
            if (t == null || t.Length < 6)
                return new Affine { m00 = 1, m11 = 1 };
            return new Affine { m00 = t[0], m01 = t[1], m02 = t[2], m10 = t[3], m11 = t[4], m12 = t[5] };
        }

        public Affine Inverse()
        {
            var det = m00 * m11 - m01 * m10;
            if (Math.Abs(det) < 1e-12)
                return new Affine { m00 = 1, m11 = 1, m02 = -m02, m12 = -m12 };
            var i00 = m11 / det;
            var i01 = -m01 / det;
            var i10 = -m10 / det;
            var i11 = m00 / det;
            return new Affine
            {
                m00 = i00, m01 = i01, m02 = -(i00 * m02 + i01 * m12),
                m10 = i10, m11 = i11, m12 = -(i10 * m02 + i11 * m12)
            };
        }

        public static Affine operator *(Affine a, Affine b)
        {
            return new Affine
            {
                m00 = a.m00 * b.m00 + a.m01 * b.m10,
                m01 = a.m00 * b.m01 + a.m01 * b.m11,
                m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02,
                m10 = a.m10 * b.m00 + a.m11 * b.m10,
                m11 = a.m10 * b.m01 + a.m11 * b.m11,
                m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12
            };
        }
    }
    static class FerryImporter
    {
        internal const string OutputRoot = "Assets/InterfaceFerry";
        // One folder of PNGs per Figma file, shared by all of its screens.
        internal const string ImageFolderName = "Images";

        /// <summary>
        /// Builds one pushed root, with text bound directly to shared font assets.
        /// </summary>
        public static object Import(FerryPayload payload)
        {
            var nodes = payload.nodes;
            var folder = FileFolder(payload.fileName);
            var imageFolder = ImageFolder(payload.fileName);

            EnsureFolder(folder);
            var missing = new List<string>();
            var keys = PushImages(nodes, imageFolder, missing, out var fresh);
            
            // import pngs
            foreach (var key in fresh)
            {
                AssetDatabase.ImportAsset(imageFolder + "/" + key + ".png", ImportAssetOptions.ForceSynchronousImport);
            }
            
            // Create Prefab
            return FerryUguiImporter.Import(payload, folder, imageFolder, keys.Count, fresh.Count, missing.Count);
        }

        internal static string FileFolder(string fileName) =>
            OutputRoot + "/" + SanitizeName(fileName);

        internal static string ImageFolder(string fileName) => FileFolder(fileName) + "/Images";

        internal static string ExistingImage(string fileName, string key)
        {
            var imageFolder = ImageFolder(fileName);
            var path = ProjectPath(imageFolder + "/" + key + ".png");
            return File.Exists(path) ? path : null;
        }

        internal static string UniqueAssetPath(string path)
        {
            var candidate = path;
            for (var i = 2; !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(candidate, AssetPathToGUIDOptions.OnlyExistingAssets)); i++)
                candidate = Path.ChangeExtension(path, null) + i + Path.GetExtension(path);
            return candidate;
        }

        internal static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;
            var parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            EnsureFolder(parent);
            // On disk but not imported yet: CreateFolder would add "<name> 1" next to it instead.
            if (Directory.Exists(ProjectPath(assetFolder)))
                AssetDatabase.ImportAsset(assetFolder, ImportAssetOptions.ForceSynchronousImport);
            else
                AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }

        // Moves this push's staged PNGs into the shared folder. A key already there is the same artwork, because
        // the key hashes the bytes, so it is left alone and a re-push does not have to upload it again.
        static HashSet<string> PushImages(FerryNode[] nodes, string imageFolder, 
            List<string> missing, out HashSet<string> fresh)
        {
            var keys = new HashSet<string>();
            fresh = new HashSet<string>();
            foreach (var node in nodes)
            {
                var key = node.image?.key;
                if (string.IsNullOrEmpty(key) || keys.Contains(key))
                    continue;
                if (missing.Contains(key))
                {
                    node.image = null;
                    continue;
                }

                var target = ProjectPath(imageFolder + "/" + key + ".png");
                CommandReferences.TryGetStagedPath(key, out var staged);
                
                if (!File.Exists(target))
                {
                    var source = staged != null && File.Exists(staged) ? staged : null;
                    if (source != null)
                    {
                        Directory.CreateDirectory(ProjectPath(imageFolder));
                        File.Copy(source, target, false);
                        fresh.Add(key);
                    }
                }
                if (staged != null && File.Exists(staged))
                    File.Delete(staged);

                if (File.Exists(target))
                    keys.Add(key);
                else
                {
                    missing.Add(key);
                    node.image = null;
                }
            }
            return keys;
        }

        // TODO:
        // Name "Menu / Item" to "MenuItem"
        // should convert "Menu / Item" to something like "menu_item"
        internal static string SanitizeName(string name)
        {
            var result = new StringBuilder();
            var normalized = (name ?? "").Normalize(NormalizationForm.FormD);
            foreach (Match token in Regex.Matches(Regex.Replace(normalized, @"\p{Mn}", ""), "[A-Za-z0-9]+"))
            {
                result.Append(char.ToUpperInvariant(token.Value[0])).Append(token.Value.Substring(1));
            }
            return result.ToString();
        }

        static string ProjectPath(string assetPath)
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(root, assetPath);
        }
    }
}

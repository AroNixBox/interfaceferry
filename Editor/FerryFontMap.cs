using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InterfaceFerry
{
    /// <summary>
    /// One Figma file's fonts: left the font exactly as Figma asks for it, right the project .ttf/.otf that draws it.
    /// Lives next to the file's screens as &lt;File&gt;.fonts.asset; every import adds the fonts it needs and keeps
    /// what is already assigned. Changing a font rebuilds that file's screens.
    /// </summary>
    class FerryFontMap : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            // First string field, so Unity's list shows it as the element's title.
            [Tooltip("The font as Figma asks for it: family / style. Written by the import.")]
            public string required;
            [Tooltip("The project font file that draws it. None leaves the text on Unity's default font.")]
            public Font font;
        }

        public List<Entry> entries = new List<Entry>();

        // What the screens were last built with, so OnValidate rebuilds on a real change only, not on load.
        [NonSerialized] string m_Built;

        public static string Required(string family, string style) => (family ?? "").Trim() + " / " + (style ?? "").Trim();

        /// <summary>
        /// The map in an import folder, created when the folder has none yet.
        /// </summary>
        public static FerryFontMap LoadOrCreate(string folder, string fileName)
        {
            var path = folder + "/" + fileName + ".fonts.asset";
            var map = AssetDatabase.LoadAssetAtPath<FerryFontMap>(path);
            if (map != null)
                return map;
            map = CreateInstance<FerryFontMap>();
            AssetDatabase.CreateAsset(map, path);
            return map;
        }

        public Font Resolve(string family, string style)
        {
            var key = Required(family, style);
            foreach (var e in entries)
                if (string.Equals(e.required, key, StringComparison.OrdinalIgnoreCase))
                    return e.font;
            return null;
        }

        /// <summary>
        /// Adds a row for each font the push asks for and has no row yet, prefilled from an exactly matching file.
        /// Rows are never removed: a font a screen dropped for one push should not lose its assignment.
        /// </summary>
        public void EnsureEntries(IEnumerable<(string family, string style)> used)
        {
            var changed = false;
            foreach (var (family, style) in used)
            {
                if (string.IsNullOrEmpty(family))
                    continue;
                var key = Required(family, style);
                var entry = entries.Find(e => string.Equals(e.required, key, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    entries.Add(new Entry { required = key, font = Guess(family, style) });
                    changed = true;
                }
                else if (entry.font == null)
                {
                    // A font added to the project since the last push fills an empty row too.
                    entry.font = Guess(family, style);
                    changed |= entry.font != null;
                }
            }
            if (!changed)
                return;
            entries.Sort((a, b) => string.Compare(a.required, b.required, StringComparison.OrdinalIgnoreCase));
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
        }

        /// <summary>
        /// Called by the importer after building with this map, so the next OnValidate compares against it.
        /// </summary>
        public void MarkBuilt() => m_Built = Signature();

        void OnValidate()
        {
            var signature = Signature();
            if (m_Built == null)
            {
                m_Built = signature; // first validate is the load, not an edit
                return;
            }
            if (signature == m_Built)
                return;
            m_Built = signature;

            // Deferred: OnValidate runs inside serialization, where importing assets is not allowed.
            var path = AssetDatabase.GetAssetPath(this);
            EditorApplication.delayCall += () =>
            {
                if (string.IsNullOrEmpty(path))
                    return;
                var n = FerryImporter.RebuildFile(Path.GetFileName(Path.GetDirectoryName(path)));
                Debug.Log("[InterfaceFerry] Font mapping changed, rebuilt " + n + " screen(s).");
            };
        }

        string Signature()
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
                sb.Append(e.required).Append('=').Append(e.font != null ? AssetDatabase.GetAssetPath(e.font) : "").Append('|');
            return sb.ToString();
        }

        // Exact per style: what follows the family in the file name has to be the style itself, so "Bold" does
        // not land on ExtraBold or BoldItalic, nor "Italic" on BlackItalic. Variable fonts never match, which is
        // right: UI Toolkit draws only their default instance.
        static Font Guess(string family, string style)
        {
            var wantFamily = Squash(family);
            var wantStyle = Squash(style);
            var regular = wantStyle.Length == 0 || wantStyle == "regular";
            if (wantFamily.Length == 0)
                return null;

            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Font"))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                var name = Squash(Path.GetFileNameWithoutExtension(path));
                if (!name.StartsWith(wantFamily, StringComparison.Ordinal))
                    continue;
                var rest = name.Substring(wantFamily.Length);
                if (rest == wantStyle || (regular && (rest == "" || rest == "regular")))
                    return AssetDatabase.LoadAssetAtPath<Font>(path);
            }
            return null;
        }

        static string Squash(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in (s ?? "").ToLowerInvariant())
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            return sb.ToString();
        }

        // Populated by JsonUtility from the EditorJsonUtility PPtr round-trip, never assigned in code.
#pragma warning disable 649
        [Serializable]
        struct RawPPtr { public long fileID; public string guid; public int type; }

        [Serializable]
        struct RawContainer { public RawPPtr o; }
#pragma warning restore 649

        [Serializable]
        class PPtrContainer { public Object o; }

        /// <summary>
        /// The project://database URI UI Builder emits for an asset, as the connector's FigmaFontMap builds it.
        /// </summary>
        public static string MakeAssetUrl(Object asset)
        {
            var container = new PPtrContainer { o = asset };
            var raw = JsonUtility.FromJson<RawContainer>(EditorJsonUtility.ToJson(container)).o;

            var builder = new UriBuilder("project", "database", -1, AssetDatabase.GetAssetPath(asset))
            {
                Query = "fileID=" + raw.fileID + "&guid=" + raw.guid + "&type=" + raw.type,
                Fragment = asset.name
            };
            return "url(\"" + builder + "\")";
        }
    }
}

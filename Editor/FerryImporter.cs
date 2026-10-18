using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace InterfaceFerry
{
    /// <summary>
    /// The wire payload, one per pushed selection root. Field names match the plugin's code.js.
    /// </summary>
    [Serializable]
    class FerryPayload
    {
        public int schema;
        public string fileName;
        public float scale;
        public FerryNode[] nodes;
    }

    [Serializable]
    class FerryNode
    {
        public string id;
        public string name;
        public string type;
        public int parentIndex;
        public float width;
        public float height;
        // Figma's absoluteTransform, row-major: [m00, m01, m02, m10, m11, m12].
        public float[] t;
        // Containers and text only. Any other leaf's own opacity is baked into its PNG.
        public float opacity = 1f;
        public bool clip;
        public FerryImage image;
        public FerryText text;
    }

    /// <summary>
    /// A TEXT layer USS can draw. Spacings arrive in pixels and the characters with Figma's text case applied.
    /// </summary>
    [Serializable]
    class FerryText
    {
        public string characters;
        public string family;
        public string style;
        public float weight = 400;
        public float size = 12;
        // sRGB 0..1, alpha already multiplied by the fill's opacity.
        public float[] color;
        public string align;
        public string valign;
        public string autoResize;
        public bool truncate;
        public int maxLines;
        public float letterSpacing;
        // 0 for Figma's auto line height, which is the font's own, as in Unity.
        public float lineHeight;
        public float paragraphSpacing;
        public string decoration;
        // Height of the lines themselves; null when the plugin could not measure it (fonts missing in Figma).
        public float? contentHeight;
        public FerryShadow shadow;
    }

    [Serializable]
    class FerryShadow
    {
        public float x;
        public float y;
        public float blur;
        public float[] color;
    }

    [Serializable]
    class FerryImage
    {
        public string key;
        // The render bounds in the node's own, unrotated space: effects and strokes can reach outside the box.
        public float x;
        public float y;
        public float w;
        public float h;
    }

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

    /// <summary>
    /// Writes one pushed root as &lt;Screen&gt;.raster.uxml + .uss. PNGs live in the Figma file's shared Images/
    /// folder, named by their content hash, so screens of one file share identical artwork.
    /// Every element is absolutely placed from Figma's transforms, so auto layout, constraints and the like
    /// are not translated: their effect arrives already resolved as position, rotation and scale.
    /// </summary>
    static class FerryImporter
    {
        internal const string OutputRoot = "Assets/InterfaceFerry";
        // One folder of PNGs per Figma file, shared by all of its screens.
        internal const string ImageFolderName = "Images";
        // 0.1.0 kept every screen's PNGs in raster/<Screen>/; they move to Images/ on the screen's next build.
        const string k_LegacyFolderName = "raster";

        // Below this a rotation or scale is float noise from Figma's matrix, not a design decision.
        const double k_Epsilon = 1e-4;
        // How far the render bounds may sit off the box before the image needs its own element.
        const float k_BoundsTolerance = 0.01f;

        static readonly Regex k_UnsafeFileChars = new Regex(@"[^\p{L}\p{N} _\-\.]");

        // The last payload per screen, so a changed font mapping can rebuild screens without a re-push.
        static string PayloadDir => Path.Combine(ProjectPath("Library"), "InterfaceFerry", "payloads");

        /// <summary>
        /// Builds one pushed root. <paramref name="json"/> is the payload as sent, kept for <see cref="RebuildFile"/>.
        /// </summary>
        public static object Import(FerryPayload payload, string json)
        {
            var nodes = payload.nodes;
            var fileName = SafeName(payload.fileName, "Figma");
            var screenName = SafeName(nodes[0].name, "Screen");

            var folder = OutputRoot + "/" + fileName;
            var imageFolder = folder + "/" + ImageFolderName;
            var legacyRoot = folder + "/" + k_LegacyFolderName;
            var legacyFolder = legacyRoot + "/" + screenName;
            var uxmlPath = folder + "/" + screenName + ".raster.uxml";
            var ussPath = folder + "/" + screenName + ".raster.uss";

            // Through the AssetDatabase, not only on disk: the font map is an asset and needs a known folder.
            EnsureFolder(folder);
            if (json != null)
            {
                var saved = Path.Combine(PayloadDir, fileName, screenName + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(saved));
                File.WriteAllText(saved, json);
            }

            var fonts = FerryFontMap.LoadOrCreate(folder, fileName);
            var unmapped = MapFonts(nodes, fonts);

            var missing = new List<string>();
            var keys = PromoteImages(nodes, imageFolder, legacyFolder, missing, out var fresh);

            var children = new List<int>[nodes.Length];
            for (var i = 0; i < nodes.Length; ++i)
                children[i] = new List<int>();
            for (var i = 1; i < nodes.Length; ++i)
            {
                var p = nodes[i].parentIndex;
                if (p >= 0 && p < i)
                    children[p].Add(i);
            }

            var uss = new StringBuilder();
            uss.Append("/* Generated by InterfaceFerry from \"").Append(payload.fileName).Append("\". Re-push overwrites it. */\n");
            var uxml = new StringBuilder();
            uxml.Append("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" editor-extension-mode=\"False\">\n");
            uxml.Append("    <Style src=\"").Append(Xml(screenName + ".raster.uss")).Append("\" />\n");
            Emit(0, nodes, children, ImageFolderName + "/", uxml, uss, 1, fonts);
            uxml.Append("</ui:UXML>\n");

            File.WriteAllText(ProjectPath(ussPath), uss.ToString());
            File.WriteAllText(ProjectPath(uxmlPath), uxml.ToString());

            // PNGs first: the USS resolves its url()s on import, and the UXML resolves the USS.
            // Only new files: a PNG already in the folder is the same artwork and stays as it is.
            if (fresh.Count > 0)
                EnsureFolder(imageFolder);
            foreach (var key in fresh)
                AssetDatabase.ImportAsset(imageFolder + "/" + key + ".png", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ussPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(uxmlPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.IsValidFolder(legacyFolder))
                AssetDatabase.DeleteAsset(legacyFolder);
            else if (Directory.Exists(ProjectPath(legacyFolder)))
            {
                // On disk only, never imported: nothing in the AssetDatabase to delete it through.
                Directory.Delete(ProjectPath(legacyFolder), true);
                File.Delete(ProjectPath(legacyFolder) + ".meta");
            }
            DeleteIfEmpty(legacyRoot);
            var removed = CollectUnreferenced(imageFolder);
            fonts.MarkBuilt();

            var notes = new List<string>();
            if (missing.Count > 0)
                notes.Add(missing.Count + " image(s) were not uploaded: " + string.Join(", ", missing));
            if (unmapped.Count > 0)
                notes.Add(unmapped.Count + " font(s) use Unity's default font, map them in " + AssetDatabase.GetAssetPath(fonts)
                    + ": " + string.Join(", ", unmapped));
            foreach (var note in notes)
                Debug.LogWarning("[InterfaceFerry] " + note);
            Debug.Log("[InterfaceFerry] " + nodes.Length + " nodes, " + keys.Count + " images (" + fresh.Count + " new, "
                + removed + " unreferenced removed) -> " + uxmlPath);

            return new { ok = true, uxml = uxmlPath, uss = ussPath, nodes = nodes.Length, images = keys.Count,
                newImages = fresh.Count, removedImages = removed, notes };
        }

        /// <summary>
        /// Re-runs the last push of every screen of one Figma file, by its import folder's name, against the
        /// current font map. Images are already in place.
        /// </summary>
        public static int RebuildFile(string fileFolderName)
        {
            var dir = Path.Combine(PayloadDir, fileFolderName ?? "");
            if (string.IsNullOrEmpty(fileFolderName) || !Directory.Exists(dir))
                return 0;
            var n = 0;
            foreach (var path in Directory.GetFiles(dir, "*.json"))
            {
                var json = File.ReadAllText(path);
                var payload = JsonConvert.DeserializeObject<FerryPayload>(json);
                if (payload?.nodes == null || payload.nodes.Length == 0 || payload.schema != FerryCommands.Schema)
                    continue;
                Import(payload, null);
                n++;
            }
            return n;
        }

        // Adds the push's fonts to the map, and names those still without a project font.
        static List<string> MapFonts(FerryNode[] nodes, FerryFontMap fonts)
        {
            var used = new List<(string, string)>();
            foreach (var node in nodes)
                if (node.text != null)
                    used.Add((node.text.family, node.text.style));
            fonts.EnsureEntries(used);

            var unmapped = new List<string>();
            foreach (var (family, style) in used)
            {
                var label = FerryFontMap.Required(family, style);
                if (fonts.Resolve(family, style) == null && !unmapped.Contains(label))
                    unmapped.Add(label);
            }
            return unmapped;
        }

        static void EnsureFolder(string assetFolder)
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
        static HashSet<string> PromoteImages(FerryNode[] nodes, string imageFolder, string legacyFolder,
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
                var staged = FerryCommands.StagedPath(key);
                var legacy = ProjectPath(legacyFolder + "/" + key + ".png");
                if (!File.Exists(target))
                {
                    var source = staged != null && File.Exists(staged) ? staged : File.Exists(legacy) ? legacy : null;
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

        // The old raster/ goes once the last screen has moved out of it.
        static void DeleteIfEmpty(string assetFolder)
        {
            var dir = ProjectPath(assetFolder);
            if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).GetEnumerator().MoveNext())
                return;
            if (!AssetDatabase.IsValidFolder(assetFolder) || !AssetDatabase.DeleteAsset(assetFolder))
            {
                Directory.Delete(dir);
                File.Delete(dir + ".meta");
            }
        }

        // Asset types that can point at a PNG: style sheets, UXML, prefabs, scenes, materials, other assets.
        static readonly HashSet<string> k_ReferencingExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".uss", ".tss", ".uxml", ".prefab", ".unity", ".mat", ".asset", ".controller", ".anim"
        };

        /// <summary>
        /// Deletes the PNGs of a shared Images folder that nothing in the project refers to any more, by Unity's
        /// own dependency data, so a PNG reused in hand-made UI stays. Returns how many were removed.
        /// </summary>
        static int CollectUnreferenced(string imageFolder)
        {
            if (!AssetDatabase.IsValidFolder(imageFolder))
                return 0;

            var pngs = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { imageFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path)?.Replace('\\', '/') == imageFolder)
                    pngs.Add(path);
            }
            if (pngs.Count == 0)
                return 0;

            var referrers = new List<string>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
                if (path.StartsWith("Assets/", StringComparison.Ordinal) && k_ReferencingExtensions.Contains(Path.GetExtension(path)))
                    referrers.Add(path);
            var used = new HashSet<string>(AssetDatabase.GetDependencies(referrers.ToArray(), false));

            var removed = 0;
            foreach (var png in pngs)
                if (!used.Contains(png) && AssetDatabase.DeleteAsset(png))
                    removed++;
            return removed;
        }

        static void Emit(int index, FerryNode[] nodes, List<int>[] children, string imageUrlBase,
            StringBuilder uxml, StringBuilder uss, int depth, FerryFontMap fonts)
        {
            var node = nodes[index];
            var cls = ClassOf(node.id);
            var indent = new string(' ', depth * 4);

            // Relative to the parent through its inverse, not node.x/y: a GROUP does not reset the origin,
            // and a rotated parent turns its children's axes with it.
            var abs = Affine.From(node.t);
            var rel = index == 0 || node.parentIndex < 0
                ? new Affine { m00 = abs.m00, m01 = abs.m01, m10 = abs.m10, m11 = abs.m11 }
                : Affine.From(nodes[node.parentIndex].t).Inverse() * abs;

            uss.Append('.').Append(cls).Append(" {\n");
            Prop(uss, "position", "absolute");
            Prop(uss, "left", Px(rel.m02));
            Prop(uss, "top", Px(rel.m12));
            Prop(uss, "width", Px(node.width));
            Prop(uss, "height", Px(node.height));
            AppendTransform(uss, rel);
            if (node.opacity < 1f - k_Epsilon)
                Prop(uss, "opacity", Num(Mathf.Clamp01(node.opacity)));
            if (node.clip)
                Prop(uss, "overflow", "hidden");

            var image = node.image;
            var ownElement = image != null && !FillsBox(image, node);
            if (image != null && !ownElement)
                AppendBackground(uss, imageUrlBase + image.key + ".png");
            if (node.text != null)
                AppendText(uss, node, fonts);
            uss.Append("}\n");

            if (ownElement)
            {
                uss.Append('.').Append(cls).Append("__img {\n");
                Prop(uss, "position", "absolute");
                Prop(uss, "left", Px(image.x));
                Prop(uss, "top", Px(image.y));
                Prop(uss, "width", Px(image.w));
                Prop(uss, "height", Px(image.h));
                AppendBackground(uss, imageUrlBase + image.key + ".png");
                uss.Append("}\n");
            }

            if (node.text != null)
            {
                var rich = RichText(node.text, out var text);
                uxml.Append(indent).Append("<ui:Label name=\"").Append(Xml(node.name))
                    .Append("\" class=\"").Append(cls).Append("\" text=\"").Append(XmlAttr(text))
                    .Append("\" enable-rich-text=\"").Append(rich ? "true" : "false").Append("\" />\n");
                return;
            }

            uxml.Append(indent).Append("<ui:VisualElement name=\"").Append(Xml(node.name))
                .Append("\" class=\"").Append(cls).Append('"');

            var kids = children[index];
            if (!ownElement && kids.Count == 0)
            {
                uxml.Append(" />\n");
                return;
            }
            uxml.Append(">\n");
            // First child, so the node's own pixels sit under its children as they do in Figma.
            if (ownElement)
                uxml.Append(indent).Append("    <ui:VisualElement name=\"__raster\" class=\"").Append(cls)
                    .Append("__img\" picking-mode=\"Ignore\" />\n");
            foreach (var child in kids)
                Emit(child, nodes, children, imageUrlBase, uxml, uss, depth + 1, fonts);
            uxml.Append(indent).Append("</ui:VisualElement>\n");
        }

        // Figma transforms are rotation and flip only, so a QR split is exact: R(angle) * diag(sx, sy),
        // with a flip landing on sy. USS applies scale, then rotate, around transform-origin, which matches
        // the matrix once the origin is the top-left corner Figma's translation points at.
        static void AppendTransform(StringBuilder uss, Affine m)
        {
            var sx = Math.Sqrt(m.m00 * m.m00 + m.m10 * m.m10);
            if (sx < 1e-9)
                return;
            var sy = (m.m00 * m.m11 - m.m01 * m.m10) / sx;
            var angle = Math.Atan2(m.m10, m.m00) * 180.0 / Math.PI;
            // A horizontal flip comes out as 180deg plus a vertical flip; the same matrix, read as scale -1 1.
            if (sy < 0 && Math.Abs(angle) > 90)
            {
                angle -= Math.Sign(angle) * 180.0;
                sx = -sx;
                sy = -sy;
            }

            var rotated = Math.Abs(angle) > k_Epsilon;
            var scaled = Math.Abs(sx - 1) > k_Epsilon || Math.Abs(sy - 1) > k_Epsilon;
            if (!rotated && !scaled)
                return;

            Prop(uss, "transform-origin", "left top");
            if (rotated)
                Prop(uss, "rotate", Num(angle) + "deg");
            if (scaled)
                Prop(uss, "scale", Num(sx) + " " + Num(sy));
        }

        // The Label fills the Figma text box, so position and transforms stay those of the box. Inside it, the
        // lines are centred on a band as tall as Figma's lines: Figma pads the first line by half the leading,
        // Unity does not, and centring a block of Figma's height puts that same half above it.
        static void AppendText(StringBuilder uss, FerryNode node, FerryFontMap fonts)
        {
            var t = node.text;
            Prop(uss, "margin", "0");
            Prop(uss, "border-width", "0");

            var vertical = "middle";
            float padTop = 0, padBottom = 0;
            var grows = t.autoResize == "HEIGHT" || t.autoResize == "WIDTH_AND_HEIGHT";
            if (!grows && t.valign != "CENTER")
            {
                var content = t.contentHeight ?? -1f;
                if (content < 0 || content > node.height + k_BoundsTolerance)
                    vertical = t.valign == "BOTTOM" ? "lower" : "upper"; // unmeasured or overflowing: Unity's own anchor
                else if (t.valign == "BOTTOM")
                    padTop = node.height - content;
                else
                    padBottom = node.height - content;
            }
            Prop(uss, "padding", Px(padTop) + " 0 " + Px(padBottom) + " 0");
            Prop(uss, "-unity-text-align", vertical + "-" + Horizontal(t.align));

            Prop(uss, "font-size", Px(t.size));
            Prop(uss, "color", Rgba(t.color));
            var font = fonts.Resolve(t.family, t.style);
            if (font != null)
                Prop(uss, "-unity-font-definition", FerryFontMap.MakeAssetUrl(font));
            // A mapped font already is that style; synthesising bold on it would double it. Only Unity's
            // default font, standing in for an unmapped one, gets weight and italics synthesised.
            var bold = font == null && t.weight >= 600;
            var italic = font == null && (t.style ?? "").IndexOf("italic", StringComparison.OrdinalIgnoreCase) >= 0;
            Prop(uss, "-unity-font-style", bold && italic ? "bold-and-italic" : bold ? "bold" : italic ? "italic" : "normal");
            if (Math.Abs(t.letterSpacing) > k_Epsilon)
                Prop(uss, "letter-spacing", Px(t.letterSpacing));
            if (t.paragraphSpacing > k_Epsilon)
                Prop(uss, "-unity-paragraph-spacing", Px(t.paragraphSpacing));

            var singleLine = t.autoResize == "WIDTH_AND_HEIGHT" || (t.truncate && t.maxLines == 1);
            Prop(uss, "white-space", singleLine ? "nowrap" : "normal");
            if (t.truncate)
            {
                Prop(uss, "overflow", "hidden");
                Prop(uss, "text-overflow", "ellipsis");
            }
            if (t.shadow != null)
                Prop(uss, "text-shadow", Px(t.shadow.x) + " " + Px(t.shadow.y) + " " + Px(t.shadow.blur) + " " + Rgba(t.shadow.color));
        }

        static string Horizontal(string align)
        {
            if (align == "CENTER")
                return "center";
            return align == "RIGHT" ? "right" : "left"; // USS cannot justify
        }

        // Rich text only where USS falls short: line height and decoration. The characters themselves go in a
        // noparse block, so a "<" typed in Figma stays a "<".
        static bool RichText(FerryText t, out string text)
        {
            var characters = (t.characters ?? "").Replace("\r", "");
            var underline = t.decoration == "UNDERLINE";
            var strike = t.decoration == "STRIKETHROUGH";
            if (t.lineHeight <= k_Epsilon && !underline && !strike)
            {
                text = characters;
                return false;
            }

            var sb = new StringBuilder();
            if (t.lineHeight > k_Epsilon)
                sb.Append("<line-height=").Append(Px(t.lineHeight)).Append('>');
            if (underline)
                sb.Append("<u>");
            if (strike)
                sb.Append("<s>");
            sb.Append("<noparse>").Append(characters.Replace("</noparse>", "<\u200B/noparse>")).Append("</noparse>");
            if (strike)
                sb.Append("</s>");
            if (underline)
                sb.Append("</u>");
            text = sb.ToString();
            return true;
        }

        static string Rgba(float[] c)
        {
            if (c == null || c.Length < 3)
                return "rgba(0, 0, 0, 1)";
            int Channel(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
            var a = c.Length > 3 ? Mathf.Clamp01(c[3]) : 1f;
            return "rgba(" + Channel(c[0]) + ", " + Channel(c[1]) + ", " + Channel(c[2]) + ", " + Num(a) + ")";
        }

        static void AppendBackground(StringBuilder uss, string url)
        {
            Prop(uss, "background-image", "url(\"" + url + "\")");
            Prop(uss, "-unity-background-scale-mode", "stretch-to-fill");
        }

        static bool FillsBox(FerryImage image, FerryNode node)
        {
            return Mathf.Abs(image.x) < k_BoundsTolerance && Mathf.Abs(image.y) < k_BoundsTolerance
                && Mathf.Abs(image.w - node.width) < k_BoundsTolerance && Mathf.Abs(image.h - node.height) < k_BoundsTolerance;
        }

        static void Prop(StringBuilder uss, string key, string value)
        {
            uss.Append("    ").Append(key).Append(": ").Append(value).Append(";\n");
        }

        // Not rounded to whole pixels like the connector does: the point here is that positions match exactly.
        static string Px(double v) => Num(v) + "px";

        static string Num(double v)
        {
            var r = Math.Round(v, 3);
            if (Math.Abs(r) < 0.0005)
                r = 0;
            return r.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // Unique per node and a valid USS class: ids look like "12:34" or "I12:34;56:78" inside instances.
        static string ClassOf(string id)
        {
            var s = (id ?? "").Replace(':', '-').Replace(';', '_');
            return "fr-" + Regex.Replace(s, "[^A-Za-z0-9_-]", "");
        }

        internal static string SafeName(string name, string fallback)
        {
            var s = k_UnsafeFileChars.Replace(name ?? "", "_").Trim().Trim('.');
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        static string Xml(string s) => SecurityElement.Escape(s ?? "");

        // An attribute value turns a raw line break into a space when parsed, so breaks travel as references.
        static string XmlAttr(string s) => Xml(s).Replace("\n", "&#10;").Replace("\t", "&#9;");

        static string ProjectPath(string assetPath)
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(root, assetPath);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = System.Object;

namespace InterfaceFerry
{
    static class FerryUguiImporter
    {
        internal static object Import(FerryPayload payload, string folder, string imageFolder,
            int totalImagesCount, int newImagesCount, int missingImagesCount)
        {
            var containsText = payload.nodes.Any(e => e.text != null);
            var tmpImported = Resources.Load<TMP_Settings>("TMP Settings") != null;
            if (containsText && !tmpImported)
            {
                TMP_PackageResourceImporter.ImportResources(true, false, false);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (Resources.Load<TMP_Settings>("TMP Settings") == null)
                    throw new InvalidOperationException("TextMesh Pro essentials are being installed. Push the selection again when Unity finishes importing them.");
            }

            var rootNodeName = payload.nodes[0].name;

            var canvasObject = new GameObject(rootNodeName + "Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.referencePixelsPerUnit = 100;

            var scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(payload.nodes[0].width, payload.nodes[0].height);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            
            var viewport = Rect("Viewport", canvasObject.transform, payload.nodes[0].width, payload.nodes[0].height);
            viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.anchoredPosition = Vector2.zero;
            
            var objects = new RectTransform[payload.nodes.Length];
            var fonts = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);


            for (var i = 0; i < payload.nodes.Length; i++)
            {
                var node = payload.nodes[i];
                var root = i == 0 || node.parentIndex < 0;
                if (!root && (node.parentIndex >= i || node.parentIndex >= objects.Length))
                {
                    throw new ArgumentException("Invalid parentIndex at " + node.name);
                }
                
                var parent = root 
                    ? viewport 
                    : objects[node.parentIndex];
                var rect = Rect(node.name, parent, node.width, node.height);
                objects[i] = rect;
                if (root) rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                var absolute = Affine.From(node.t);
                var local = root ? new Affine { m00 = absolute.m00, m01 = absolute.m01, m10 = absolute.m10, m11 = absolute.m11 }
                    : Affine.From(payload.nodes[node.parentIndex].t).Inverse() * absolute;
                Transform(rect, local);

                // Add visual components before clipping and opacity helpers.
                if (node.image != null)
                {
                    var image = node.image;
                    var overhang = Mathf.Abs(image.x) > 0.01f || Mathf.Abs(image.y) > 0.01f
                        || Mathf.Abs(image.w - node.width) > 0.01f || Mathf.Abs(image.h - node.height) > 0.01f;
                    var imageRect = overhang || node.clip ? Rect("__raster", rect, image.w, image.h) : rect;
                    if (overhang) imageRect.anchoredPosition = new Vector2(image.x, -image.y);
                    var graphic = imageRect.gameObject.AddComponent<UnityEngine.UI.Image>();
                    UnityEditorInternal.ComponentUtility.MoveComponentUp(graphic);
                    graphic.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(imageFolder + "/" + image.key + ".png");
                    graphic.type = UnityEngine.UI.Image.Type.Simple;
                    graphic.useSpriteMesh = false;
                    graphic.preserveAspect = false;
                    graphic.raycastTarget = !overhang;
                    if (graphic.sprite == null) throw new InvalidOperationException("Sprite was not imported: " + image.key);
                }
                if (node.text != null)
                {
                    Text(node.clip 
                        ? Rect("__text", rect, node.width, node.height) 
                        : rect, node, folder, fonts);
                }

                if (node.clip)
                {
                    var maskGraphic = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
                    UnityEditorInternal.ComponentUtility.MoveComponentUp(maskGraphic);
                    maskGraphic.raycastTarget = false;
                    rect.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
                }
                if (node.opacity < 0.9999f) rect.gameObject.AddComponent<CanvasGroup>().alpha = Mathf.Clamp01(node.opacity);
                if (node.onClick) Button(rect);
            }

            foreach (var font in fonts.Values)
            {
                if (AssetDatabase.GetAssetPath(font).StartsWith(folder + "/Fonts/", StringComparison.Ordinal))
                {
                    EditorUtility.SetDirty(font);
                    foreach (var atlas in font.atlasTextures) if (atlas != null) EditorUtility.SetDirty(atlas);
                    AssetDatabase.SaveAssetIfDirty(font);
                }
            }
             
            var localPath = folder + "/" + 
                            FerryImporter.SanitizeName(rootNodeName) + ".prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                canvasObject,
                localPath,
                InteractionMode.UserAction,
                out var success);
            
            UnityEngine.Object.DestroyImmediate(canvasObject);
            
            return new { ok = success, prefab = localPath, nodes = payload.nodes.Length, totalImagesCount = totalImagesCount,
                newImagesCount = newImagesCount, missingImagesCount = missingImagesCount };
        }

        static void Button(RectTransform rect)
        {
            var graphic = rect.GetComponent<UnityEngine.UI.Graphic>();
            if (graphic == null)
            {
                graphic = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
                graphic.color = Color.clear;
                UnityEditorInternal.ComponentUtility.MoveComponentUp(graphic);
            }
            graphic.raycastTarget = true;

            // Only the original Figma bounds receive clicks, not a shadow's raster overhang.
            var raster = rect.Find("__raster");
            if (raster != null && raster.TryGetComponent<UnityEngine.UI.Graphic>(out var rasterGraphic))
                rasterGraphic.raycastTarget = false;

            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = graphic;
            // Figma's appearance stays unchanged until explicit button states are configured.
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            while (rect.GetComponents<Component>()[1] != button)
                if (!UnityEditorInternal.ComponentUtility.MoveComponentUp(button)) break;
        }

        static RectTransform Rect(string name, Transform parent, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        static void Transform(RectTransform rect, Affine m)
        {
            var sx = Math.Sqrt(m.m00 * m.m00 + m.m10 * m.m10);
            var sy = sx < 1e-9 ? Math.Sqrt(m.m01 * m.m01 + m.m11 * m.m11)
                : (m.m00 * m.m11 - m.m01 * m.m10) / sx;
            var angle = sx < 1e-9 ? 0 : Math.Atan2(m.m10, m.m00) * 180 / Math.PI;
            if (sy < 0 && Math.Abs(angle) > 90)
            {
                angle -= Math.Sign(angle) * 180;
                sx = -sx;
                sy = -sy;
            }
            rect.anchoredPosition = new Vector2((float)m.m02, -(float)m.m12);
            rect.localRotation = Quaternion.Euler(0, 0, -(float)angle);
            rect.localScale = new Vector3((float)sx, (float)sy, 1);
        }

        static void Text(RectTransform rect, FerryNode node, string folder,
            Dictionary<string, TMP_FontAsset> fonts)
        {
            if (node.text.shadow == null)
            {
                CreateText(rect, node, folder, fonts);
                return;
            }
            var data = node.text.shadow;
            var shadowRect = Rect("__shadow", rect, node.width, node.height);
            shadowRect.anchoredPosition = new Vector2(data.x, -data.y);
            var shadow = CreateText(shadowRect, node, folder, fonts);
            shadow.color = ColorOf(data.color);
            shadow.ForceMeshUpdate(true);
            CreateText(Rect("__text", rect, node.width, node.height), node, folder, fonts);
        }

        static TextMeshProUGUI CreateText(RectTransform rect, FerryNode node, string folder,
            Dictionary<string, TMP_FontAsset> fonts)
        {
            var data = node.text;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            UnityEditorInternal.ComponentUtility.MoveComponentUp(text);
            text.font = Font(data, folder, fonts);
            text.fontSize = data.size;
            text.enableAutoSizing = false;
            text.fontStyle = FontStyles.Normal;
            text.color = ColorOf(data.color);
            text.raycastTarget = false;
            text.parseCtrlCharacters = false;
            text.textWrappingMode = data.autoResize == "WIDTH_AND_HEIGHT" || (data.truncate && data.maxLines == 1)
                ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            text.overflowMode = data.truncate ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            if (data.truncate && data.maxLines > 0) text.maxVisibleLines = data.maxLines;
            text.characterSpacing = data.size > 0 ? data.letterSpacing * 100 / data.size : 0;
            text.paragraphSpacing = data.size > 0 ? data.paragraphSpacing * 100 / data.size : 0;
            var prefix = data.lineHeight > 0 ? "<line-height=" + data.lineHeight.ToString("0.###", CultureInfo.InvariantCulture) + "px>" : "";
            if (data.decoration == "UNDERLINE") prefix += "<u>";
            if (data.decoration == "STRIKETHROUGH") prefix += "<s>";
            text.text = prefix + "<noparse>" + (data.characters ?? "").Replace("\r", "").Replace("</noparse>", "<\u200B/noparse>") + "</noparse>"
                + (data.decoration == "UNDERLINE" ? "</u>" : data.decoration == "STRIKETHROUGH" ? "</s>" : "")
                + (data.lineHeight > 0 ? "</line-height>" : "");
            var vertical = "Midline";
            var content = data.contentHeight ?? -1;
            var grows = data.autoResize == "HEIGHT" || data.autoResize == "WIDTH_AND_HEIGHT";
            if (!grows && data.valign != "CENTER")
            {
                if (content < 0 || content > node.height + 0.01f) vertical = data.valign == "BOTTOM" ? "Bottom" : "Top";
                else text.margin = data.valign == "BOTTOM" ? new Vector4(0, node.height - content, 0, 0) : new Vector4(0, 0, 0, node.height - content);
            }
            var horizontal = data.align == "CENTER" ? "" : data.align == "RIGHT" ? "Right" : "Left";
            var alignment = vertical + horizontal;
            if (vertical == "Midline" && horizontal == "") alignment = "Midline";
            else if (horizontal == "") alignment = vertical;
            text.alignment = (TextAlignmentOptions)Enum.Parse(typeof(TextAlignmentOptions), alignment);
            text.ForceMeshUpdate(true);
            return text;
        }

        static TMP_FontAsset Font(FerryText data, string folder, Dictionary<string, TMP_FontAsset> fonts)
        {
            var key = FerryFontAssets.Required(data.family, data.style);
            if (fonts.TryGetValue(key, out var cached)) return cached;
            var mapping = FerryFontAssets.LoadOrCreate(folder + "/Fonts", key);
            var font = mapping.font;
            if (font == null)
            {
                var fallback = TMP_Settings.defaultFontAsset ?? Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                if (fallback != null) return fonts[key] = fallback;
                throw new InvalidOperationException("No default TMP font is available. Assign the font for " + key + " and re-import.");
            }
            var fontFolder = folder + "/Fonts";
            FerryImporter.EnsureFolder(fontFolder);
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { fontFolder }))
            {
                var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (existing != null && existing.sourceFontFile == font) return fonts[key] = existing;
            }
            var asset = TMP_FontAsset.CreateFontAsset(font);
            if (asset == null) throw new InvalidOperationException("Could not create TMP font for " + key);
            asset.name = font.name;
            var path = FerryImporter.UniqueAssetPath(fontFolder + "/" + FerryImporter.SanitizeName(font.name) + "TMP.asset");
            AssetDatabase.CreateAsset(asset, path);
            if (asset.material != null) AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (var atlas in asset.atlasTextures) if (atlas != null) AssetDatabase.AddObjectToAsset(atlas, asset);
            return fonts[key] = asset;
        }

        static Color ColorOf(float[] value) => value == null || value.Length < 3 ? Color.white
            : new Color(value[0], value[1], value[2], value.Length > 3 ? value[3] : 1);
    }
}

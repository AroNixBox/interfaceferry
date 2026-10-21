using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ReceiverChecks
{
    public static string Run()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "InterfaceFerry.Editor");
        var modeType = assembly.GetType("InterfaceFerry.FerryReceiveMode");
        Check(modeType != null, "UI Toolkit/uGUI receiver selection is missing");
        var importer = assembly.GetType("InterfaceFerry.FerryImporter");
        var commands = assembly.GetType("InterfaceFerry.FerryCommands");
        const string file = "__FerryReceiverChecks";
        const string folder = "Assets/InterfaceFerry/FerryReceiverChecks";
        Check(!AssetDatabase.IsValidFolder(folder), "Test output already exists; refusing to overwrite it");
        var original = SceneManager.GetActiveScene();
        var originalDirty = original.isDirty;
        var key = "img-check-" + Guid.NewGuid().ToString("N");
        var texture = new Texture2D(32, 16);
        texture.SetPixels(Enumerable.Repeat(new Color(0.2f, 0.7f, 0.8f, 0.8f), 512).ToArray());
        texture.Apply();
        var png = Convert.ToBase64String(texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        var json = new JObject
        {
            ["schema"] = 2, ["fileName"] = file, ["scale"] = 2,
            ["nodes"] = JArray.Parse("[{\"id\":\"root\",\"name\":\"Receiver Screen\",\"parentIndex\":-1,\"width\":200,\"height\":100,\"opacity\":0.5,\"t\":[1,0,100,0,1,200]}, {\"id\":\"child\",\"name\":\"Rotated\",\"parentIndex\":0,\"width\":32,\"height\":16,\"clip\":true,\"t\":[0,-1,110,1,0,220]}, {\"id\":\"grandchild\",\"name\":\"Nested\",\"parentIndex\":1,\"width\":5,\"height\":6,\"t\":[0,-1,100,1,0,225]}, {\"id\":\"text\",\"name\":\"Caption\",\"parentIndex\":0,\"width\":180,\"height\":30,\"t\":[1,0,110,0,1,260],\"text\":{\"characters\":\"Hello <Ferry>\",\"family\":\"Check\",\"style\":\"Regular\",\"size\":16,\"color\":[1,1,1,1],\"align\":\"LEFT\",\"valign\":\"TOP\"}}]")
        };
        json["nodes"][0]["image"] = new JObject { ["key"] = key, ["x"] = -3, ["y"] = -4, ["w"] = 206, ["h"] = 108 };
        json["nodes"][1]["image"] = new JObject { ["key"] = key, ["x"] = 0, ["y"] = 0, ["w"] = 32, ["h"] = 16 };
        json["nodes"][3]["text"]["shadow"] = new JObject { ["x"] = 2, ["y"] = 3, ["blur"] = 0, ["color"] = new JArray(0, 0, 0, 0.5) };
        object Import(string mode)
        {
            try
            {
                return commands.GetMethod("Import").Invoke(null, new object[] { json, mode });
            }
            catch (TargetInvocationException error) { throw new Exception((error.InnerException ?? error).ToString()); }
        }
        Scene opened = default;
        try
        {
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            Check(sourceFont != null, "TMP essentials must be installed to exercise font conversion");
            assembly.GetType("InterfaceFerry.FerryFontAssets").GetMethod("LoadOrCreate").Invoke(null,
                new object[] { folder + "/UITK/Fonts", "Check / Regular", sourceFont });
            commands.GetMethod("StageImage").Invoke(null, new object[] { key, png });
            var ugui = JObject.FromObject(Import("UGUI"));
            Check((bool)ugui["ok"], "uGUI import failed");
            Check((string)ugui["scene"] == folder + "/UGUI/ReceiverScreen.unity", "Scene naming/layout differs");
            Check(SceneManager.GetActiveScene() == original && original.isDirty == originalDirty, "Import changed the active scene or dirty state");
            var spritePath = folder + "/UGUI/Images/" + key + ".png";
            var spriteImporter = (TextureImporter)AssetImporter.GetAtPath(spritePath);
            Check(spriteImporter.textureType == TextureImporterType.Sprite && spriteImporter.spriteImportMode == SpriteImportMode.Single, "uGUI PNG is not a Single Sprite");
            var spriteSettings = new TextureImporterSettings();
            spriteImporter.ReadTextureSettings(spriteSettings);
            Check(spriteSettings.spriteMeshType == SpriteMeshType.FullRect, "Sprite is not Full Rect");
            var missingInOtherMode = JObject.FromObject(commands.GetMethod("MissingImages").Invoke(null, new object[] { file, new[] { key }, "UITK" }));
            Check(((JArray)missingInOtherMode["missing"]).Count == 0, "Existing PNG bytes should be reused across receivers without another upload");
            opened = EditorSceneManager.OpenScene((string)ugui["scene"], OpenSceneMode.Additive);
            var camera = opened.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).SingleOrDefault();
            Check(camera != null && camera.enabled && camera.gameObject.activeInHierarchy && camera.targetTexture == null
                && camera.targetDisplay == 0, "Generated scene has no active display camera");
            Check(camera.clearFlags == CameraClearFlags.SolidColor && camera.backgroundColor == Color.black
                && camera.cullingMask == 0, "UI camera must clear the background without rendering scene geometry");
            var all = opened.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RectTransform>(true)).ToArray();
            var viewport = all.Single(t => t.name == "Viewport");
            Check(viewport.sizeDelta == new Vector2(9584, 1432), "Fixed viewport resolution differs");
            Check(!opened.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnityEngine.UI.CanvasScaler>(true)).Any(), "CanvasScaler was added");
            var root = all.Single(t => t.name == "Receiver Screen");
            Check(root.anchoredPosition == Vector2.zero && root.pivot == new Vector2(0.5f, 0.5f) && root.anchorMin == new Vector2(0.5f, 0.5f), "Root rectangle is not centred");
            Check(Mathf.Approximately(root.GetComponent<CanvasGroup>().alpha, 0.5f), "Container opacity was not preserved");
            var rootCenter = root.TransformPoint(root.rect.center);
            Check(Vector3.Distance(rootCenter, viewport.TransformPoint(viewport.rect.center)) < 0.001f, "Root visual centre differs from screen centre");
            var child = all.Single(t => t.name == "Rotated");
            Check(Vector2.Distance(child.anchoredPosition, new Vector2(10, -20)) < 0.001f && Mathf.Abs(Mathf.DeltaAngle(child.localEulerAngles.z, -90)) < 0.001f, "Rotated child transform differs");
            Check(child.GetComponent<UnityEngine.UI.Mask>() != null && !child.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic
                && child.GetComponent<UnityEngine.UI.RectMask2D>() == null, "Rotated container requires a transform-aware stencil mask");
            Check(Vector2.Distance(all.Single(t => t.name == "Nested").anchoredPosition, new Vector2(5, -10)) < 0.001f, "Nested local position differs");
            var raster = root.GetChild(0).GetComponent<RectTransform>();
            Check(raster.name == "__raster" && raster.anchoredPosition == new Vector2(-3, 4) && raster.sizeDelta == new Vector2(206, 108), "Raster bounds/order differ");
            Check(raster.GetComponent<UnityEngine.UI.Image>()?.sprite != null && raster.GetComponent<UnityEngine.UI.RawImage>() == null, "Raster is not an Image with a sprite");
            var caption = all.Single(t => t.name == "Caption");
            Check(caption.Find("__text") != null && caption.Find("__shadow") != null, "Text shadow requires separate renderable TMP geometry");
            var foreground = caption.Find("__text").GetComponent<TMPro.TextMeshProUGUI>();
            var shadow = caption.Find("__shadow").GetComponent<TMPro.TextMeshProUGUI>();
            Check(foreground != null && shadow != null && shadow.transform.GetSiblingIndex() < foreground.transform.GetSiblingIndex(), "Text shadow must use renderable TMP geometry behind the foreground");
            Check(shadow.rectTransform.anchoredPosition == new Vector2(2, -3) && shadow.color == new Color(0, 0, 0, 0.5f), "Text shadow offset/color differ");
            Check(foreground.font.sourceFontFile == sourceFont, "uGUI did not use the shared font mapping");
            Check(AssetDatabase.GetAssetPath(foreground.font).StartsWith(folder + "/UGUI/Fonts/"), "Generated TMP font was not persisted");
            Check(AssetDatabase.GetDependencies((string)ugui["scene"]).Contains(spritePath), "Saved scene does not retain sprite dependency");
            Render(opened);
            var rejected = false;
            try { Import("UGUI"); }
            catch (Exception error) { rejected = error.Message.Contains("Close the generated scene"); }
            Check(rejected && opened.isLoaded, "Re-import must not overwrite a loaded generated scene");
            EditorSceneManager.CloseScene(opened, true); opened = default;
            Import("UITK");
            var uitkPath = folder + "/UITK/Images/" + key + ".png";
            Check(((TextureImporter)AssetImporter.GetAtPath(uitkPath)).textureType == TextureImporterType.Default, "UITK PNG is not Default");
            Check(AssetDatabase.LoadAssetAtPath<Sprite>(spritePath) != null, "UITK import changed the existing uGUI sprite");
            var uxml = (string)JObject.FromObject(Import("UITK"))["uxml"];
            Check(uxml == folder + "/UITK/ReceiverScreen.uxml", "UXML naming/layout differs");
            Check(AssetDatabase.GetDependencies(uxml).Contains(uitkPath), "UXML does not retain UITK texture dependency");
            importer.GetMethod("CleanupImages").Invoke(null, new object[] { file });
            Check(AssetDatabase.LoadAssetAtPath<Sprite>(spritePath) != null && AssetDatabase.LoadAssetAtPath<Texture2D>(uitkPath) != null, "Cleanup deleted a referenced receiver image");
            return "PASS: both receivers, sprite/default settings, fixed viewport, centred rectangle, nested rotation, raster overhang, editable text, scene isolation, deduplication and cleanup.";
        }
        finally
        {
            if (opened.IsValid() && opened.isLoaded) EditorSceneManager.CloseScene(opened, true);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }
    }

    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }

    static void Render(Scene scene)
    {
        var canvas = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Canvas>()).Single();
        foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
        var cameraObject = new GameObject("Receiver render check", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.GetComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 200;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var render = new RenderTexture(600, 400, 24);
        var pixels = new Texture2D(600, 400, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = render;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = render;
            pixels.ReadPixels(new Rect(0, 0, 600, 400), 0, 0);
            pixels.Apply();
            Check(pixels.GetPixels().Count(c => c.g > 0.1f) > 1000, "Generated UI render is blank");
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "InterfaceFerry-UGUI.png"), pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(render);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}

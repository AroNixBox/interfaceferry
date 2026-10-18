using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace InterfaceFerry
{
    /// <summary>
    /// InterfaceFerry as Pipeline commands. One way only: the plugin stages PNGs, then sends the tree.
    /// Pipeline owns the listener, the bearer token and CORS, exactly as for the UI Toolkit Connector.
    /// </summary>
    static class FerryCommands
    {
        const string k_Tag = "interfaceferry";
        internal const int Schema = 2;

        // Same cap as the connector's figma_stage_image; Pipeline's own body cap (32 MB) is above it.
        const int k_MaxStagedImageBytes = 16 * 1024 * 1024;

        // The plugin derives keys from the bytes ("img-<hash>-<length>"), so anything else is not ours.
        static readonly Regex k_Key = new Regex("^[A-Za-z0-9_-]{1,128}$");

        static string s_StagingDir;
        static string s_ProjectName;

        // Application.dataPath is main-thread only, and staging deliberately runs off the main thread.
        [InitializeOnLoadMethod]
        static void Init()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            var root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            s_StagingDir = Path.Combine(root, "Library", "InterfaceFerry", "staging");
            s_ProjectName = Path.GetFileName(root);
        }

        internal static string StagedPath(string key)
        {
            if (string.IsNullOrEmpty(s_StagingDir) || !k_Key.IsMatch(key ?? ""))
                return null;
            return Path.Combine(s_StagingDir, key + ".png");
        }

        static bool IsPng(byte[] b)
        {
            return b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;
        }

        [CliCommand("ferry_status", "Identify this Editor to the InterfaceFerry Figma plugin.", Tags = new[] { k_Tag })]
        public static object Status()
        {
            return new { interfaceFerry = true, schemas = new[] { Schema }, project = s_ProjectName };
        }

        [CliCommand("ferry_stage_image", "Stage one PNG exported by the InterfaceFerry Figma plugin; the next ferry_import moves it into Assets.",
            // Plain file IO, no Unity API: off the main thread so uploads do not queue behind it.
            MainThreadRequired = false, Tags = new[] { k_Tag })]
        public static object StageImage(
            [CliArg("image_key", "Key the plugin derived from the PNG bytes.", Required = true)] string imageKey,
            [CliArg("png_base64", "The PNG bytes, base64 encoded.", Required = true)] string pngBase64)
        {
            var path = StagedPath(imageKey);
            if (path == null)
                throw new ArgumentException("image_key is invalid");

            // Bounded before decoding, so an oversized payload costs a length check rather than the allocation.
            if ((pngBase64 != null ? pngBase64.Length : 0) > k_MaxStagedImageBytes * 4L / 3L + 4)
                throw new ArgumentException("png_base64 exceeds " + k_MaxStagedImageBytes + " bytes decoded");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(pngBase64 ?? string.Empty);
            }
            catch (FormatException e)
            {
                throw new ArgumentException("png_base64 is not valid base64: " + e.Message);
            }
            if (bytes.Length == 0 || !IsPng(bytes))
                throw new ArgumentException("png_base64 is not a PNG");

            Directory.CreateDirectory(s_StagingDir);
            File.WriteAllBytes(path, bytes);
            return new { ok = true, bytes = bytes.Length };
        }

        [CliCommand("ferry_import", "Build UXML + USS + PNGs under Assets/InterfaceFerry from a InterfaceFerry payload.",
            Tags = new[] { k_Tag })]
        public static object Import(
            [CliArg("payload", "The raster payload, as a JSON object.", Required = true)] JObject payload)
        {
            if (payload == null)
                throw new ArgumentException("payload is required");

            var data = payload.ToObject<FerryPayload>();
            if (data == null || data.nodes == null || data.nodes.Length == 0)
                throw new ArgumentException("payload has no nodes");
            if (data.schema != Schema)
                return new { ok = false, schemaMismatch = true, error = "schema " + data.schema + " is not supported" };

            return FerryImporter.Import(data, payload.ToString(Newtonsoft.Json.Formatting.None));
        }
    }
}

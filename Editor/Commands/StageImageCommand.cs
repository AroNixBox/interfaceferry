using System;
using System.IO;
using Unity.Pipeline.Commands;

namespace InterfaceFerry.Editor.Commands
{
    static class StageImageCommand
    {
        // Same cap as the connector's figma_stage_image; Pipeline's own body cap (32 MB) is above it.
        const int MaxStagedImageBytes = 16 * 1024 * 1024;
        
        [CliCommand("ferry_stage_image", "Stage one PNG exported by the InterfaceFerry Figma plugin; the next ferry_import moves it into Assets.",
            // Plain file IO, no Unity API: off the main thread so uploads do not queue behind it.
            MainThreadRequired = false)]
        public static object StageImage(
            [CliArg("image_key", "Key the plugin derived from the PNG bytes.", Required = true)] string imageKey,
            [CliArg("png_base64", "The PNG bytes, base64 encoded.", Required = true)] string pngBase64)
        {
            if (!CommandReferences.TryGetStagedPath(imageKey, out var path))
            {
                throw new ArgumentException("image_key is invalid");
            }

            // pass before decoding, so an oversized payload costs a length check rather than the allocation.
            // --- Derived from com.unity.ui.figma 0.2.0-exp.1 (Editor/Pipeline/FigmaPipelineCommands.cs: size bound and base64 validation of StageImage).
            // --- Copyright © 2026 Unity Technologies, Unity Companion License, not MIT: see Third Party Notices.md.
            if ((pngBase64 != null ? pngBase64.Length : 0) > MaxStagedImageBytes * 4L / 3L + 4)
                throw new ArgumentException("png_base64 exceeds " + MaxStagedImageBytes + " bytes decoded");

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
            // --- End of portion derived from com.unity.ui.figma.


            if (!CommandReferences.EnsureStagingDirectory())
            {
                throw new ArgumentException("Staging directory invalid");
            }
            
            File.WriteAllBytes(path, bytes);
            return new { ok = true, bytes = bytes.Length };
        }
        
        // --- Derived from com.unity.ui.figma 0.2.0-exp.1 (Editor/Pipeline/FigmaPipelineCommands.cs: IsPng).
        // --- Copyright © 2026 Unity Technologies, Unity Companion License, not MIT: see Third Party Notices.md.
        static bool IsPng(byte[] b)
        {
            return b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                   && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;
        }
        // --- End of portion derived from com.unity.ui.figma.
    }
}
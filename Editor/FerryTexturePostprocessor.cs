using UnityEditor;

namespace InterfaceFerry
{
    /// <summary>
    /// Imports pushed PNGs pixel for pixel. The default import rounds a 491x403 export up to 512x512
    /// (nPOTScale ToNearest) and caps it at 2048, so a stretched background would land distorted or blurry.
    /// </summary>
    class FerryTexturePostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(FerryImporter.OutputRoot + "/") || !assetPath.Contains("/raster/"))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.maxTextureSize = 16384;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}

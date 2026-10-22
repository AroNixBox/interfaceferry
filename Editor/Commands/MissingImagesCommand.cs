using System;
using System.Collections.Generic;
using Unity.Pipeline.Commands;

namespace InterfaceFerry.Editor.Commands
{
    static class MissingImagesCommand
    {
        [CliCommand("ferry_missing_images", "Of the given image keys, the ones a Figma file's shared Images folder does not hold yet; the plugin uploads only those.",
            MainThreadRequired = true)]
        public static object MissingImages(
            [CliArg("file_name", "The Figma file name as the payload carries it.", Required = true)] string fileName,
            [CliArg("image_keys", "The keys of the push's PNGs.", Required = true)] string[] imageKeys)
        {
            var missing = new List<string>();
            foreach (var key in imageKeys ?? Array.Empty<string>())
            {
                if (!CommandReferences.IsValidImageKey(key))
                {
                    continue;
                }

                if (FerryImporter.ExistingImage(fileName, key) != null)
                {
                    continue;
                }
                
                missing.Add(key);
            }
                
            return new { ok = true, missing };
        }
    }
}
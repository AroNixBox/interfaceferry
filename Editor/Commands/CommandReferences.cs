using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace InterfaceFerry.Editor.Commands
{
    static class CommandReferences
    {
        public const int Schema = 2;

        // Compares Images-Names (keys)
        // img-a7bx92-18432 
        //      └ Hash  └ Num PNG-Bytes
        public static bool IsValidImageKey(string key)
        {
            var validKey = new Regex("^[A-Za-z0-9_-]{1,128}$");
            return validKey.IsMatch(key);
        }

        static string StagingDirectoryPath;
        public static string ProjectName { get; private set; }

        // Application.dataPath is main-thread only, and staging deliberately runs off the main thread.
        [InitializeOnLoadMethod]
        static void Init()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            var root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            StagingDirectoryPath = Path.Combine(root, "Library", "InterfaceFerry", "staging");
            ProjectName = Path.GetFileName(root);
        }
        
        // The Path of the folder where the images get pushed to from Figma and later pulled from Unity
        public static bool TryGetStagedPath(string key, out string stagedPath)
        {
            stagedPath = string.Empty;
            
            if (string.IsNullOrEmpty(StagingDirectoryPath))
            {
                Debug.LogError("No staged Path found.");
                return false;
            }

            if (!IsValidImageKey(key))
            {
                Debug.LogError($"The inserted key of the image {key} is not valid, is the image from another source?");
                return false;
            }

            stagedPath = Path.Combine(StagingDirectoryPath, key + ".png");
            return true;
        }

        public static bool EnsureStagingDirectory()
        {
            if (StagingDirectoryPath == string.Empty)
            {
                Debug.LogError("Staging directory path not set.");
                return false;
            }
            var info = Directory.CreateDirectory(StagingDirectoryPath);
            if (!info.Exists)
            {
                Debug.LogError("Directory couldn't be created");
                return false;
            }
            
            return true;
        }
    }
}
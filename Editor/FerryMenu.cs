using System;
using System.IO;
using UnityEditor;
using UnityEngine;
// UnityEditor has a legacy PackageInfo of its own, so the one we want is aliased rather than imported.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace InterfaceFerry
{
    /// <summary>
    /// Pairing. The plugin speaks the connector's format, "port:token", with Pipeline's own token, so the
    /// connector window's Copy token works too; this is the same thing without opening that window.
    /// </summary>
    static class FerryMenu
    {
        // --- Derived from com.unity.ui.figma 0.2.0-exp.1 (Editor/Window/FigmaConnectorWindow.cs: PipelineDescriptor, CopyToken).
        // --- Copyright © 2026 Unity Technologies, Unity Companion License, not MIT: see Third Party Notices.md.
        [Serializable]
        class PipelineDescriptor
        {
            public int pid;
            public int port;
            public string evalToken;
        }

        // The clipboard is the one place the token can leave the machine, via sync tooling, so it expires.
        const double k_ClipboardHoldSeconds = 60;
        static string s_Copied;
        static double s_ClearClipboardAt;

        [MenuItem("Tools/InterfaceFerry/Copy Pipeline Token")]
        static void CopyToken()
        {
            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "Pipeline", ".unity-pipeline-port");
            PipelineDescriptor descriptor = null;
            try
            {
                if (File.Exists(path))
                    descriptor = JsonUtility.FromJson<PipelineDescriptor>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                // A read can race Pipeline's heartbeat rewrite; trying again sees the settled file.
            }
            if (descriptor == null || descriptor.port <= 0 || string.IsNullOrEmpty(descriptor.evalToken)
                || descriptor.pid != System.Diagnostics.Process.GetCurrentProcess().Id)
            {
                Debug.LogWarning("[InterfaceFerry] Pipeline is not running, so there is no token to copy.");
                return;
            }

            s_Copied = descriptor.port + ":" + descriptor.evalToken;
            EditorGUIUtility.systemCopyBuffer = s_Copied;
            s_ClearClipboardAt = EditorApplication.timeSinceStartup + k_ClipboardHoldSeconds;
            EditorApplication.update -= ClearCopiedToken;
            EditorApplication.update += ClearCopiedToken;
            Debug.Log("[InterfaceFerry] Token for port " + descriptor.port + " copied. Paste it into the plugin within 60 s.");
        }
        // --- End of portion derived from com.unity.ui.figma.

        [MenuItem("Tools/InterfaceFerry/Clean Up Unused Images")]
        static void CleanupImages()
        {
            var n = FerryImporter.CleanupImages(null);
            Debug.Log("[InterfaceFerry] Clean up: " + n + " unreferenced image(s) removed.");
        }

        [MenuItem("Tools/InterfaceFerry/Reveal Figma Plugin")]
        static void RevealPlugin()
        {
            // Resolved from the package, not Assets: embedded, git and local installs all live elsewhere.
            var info = PackageInfo.FindForAssembly(typeof(FerryMenu).Assembly);
            if (info == null)
            {
                Debug.LogWarning("[InterfaceFerry] Package not found, so the Figma plugin cannot be located.");
                return;
            }
            EditorUtility.RevealInFinder(Path.Combine(info.resolvedPath, "FigmaPlugin~", "manifest.json"));
        }

        // --- Derived from com.unity.ui.figma 0.2.0-exp.1 (Editor/Window/FigmaConnectorWindow.cs: ClearCopiedToken).
        // --- Copyright © 2026 Unity Technologies, Unity Companion License, not MIT: see Third Party Notices.md.
        static void ClearCopiedToken()
        {
            if (EditorApplication.timeSinceStartup < s_ClearClipboardAt)
                return;

            EditorApplication.update -= ClearCopiedToken;
            if (s_Copied != null && EditorGUIUtility.systemCopyBuffer == s_Copied)
                EditorGUIUtility.systemCopyBuffer = string.Empty;
            s_Copied = null;
        }
        // --- End of portion derived from com.unity.ui.figma.
    }
}

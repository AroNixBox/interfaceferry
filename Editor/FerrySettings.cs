using System;
using UnityEditor;
using UnityEngine;

namespace InterfaceFerry
{
    enum FerryReceiveMode { UITK, UGUI }

    [FilePath("ProjectSettings/InterfaceFerrySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    sealed class FerrySettings : ScriptableSingleton<FerrySettings>
    {
        [SerializeField] internal FerryReceiveMode receiveAs = FerryReceiveMode.UITK;

        internal static FerryReceiveMode Resolve(string target)
        {
            if (string.IsNullOrEmpty(target)) return instance.receiveAs;
            if (string.Equals(target, "UITK", StringComparison.OrdinalIgnoreCase)) return FerryReceiveMode.UITK;
            if (string.Equals(target, "UGUI", StringComparison.OrdinalIgnoreCase)) return FerryReceiveMode.UGUI;
            throw new ArgumentException("target must be UITK or UGUI");
        }

        [SettingsProvider]
        static SettingsProvider Provider()
        {
            return new SettingsProvider("Project/InterfaceFerry", SettingsScope.Project)
            {
                label = "InterfaceFerry",
                guiHandler = _ =>
                {
                    EditorGUILayout.LabelField("Receive Figma selection", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    var selected = EditorGUILayout.Popup("Receive as", (int)instance.receiveAs, new[] { "UI Toolkit", "uGUI" });
                    if (EditorGUI.EndChangeCheck())
                    {
                        instance.receiveAs = (FerryReceiveMode)selected;
                        instance.Save(true);
                    }
                    EditorGUILayout.HelpBox(instance.receiveAs == FerryReceiveMode.UGUI
                        ? "Creates a separate uGUI scene with Images and editable text. Fixed 9584 × 1432 viewport, root rectangle centred, no CanvasScaler."
                        : "Creates UXML and USS. Images are received as textures.", MessageType.Info);
                }
            };
        }

        [MenuItem("Tools/InterfaceFerry/Settings")]
        static void Open() => SettingsService.OpenProjectSettings("Project/InterfaceFerry");
    }
}

using System;

namespace InterfaceFerry
{
    /// <summary>
    /// An editable text layer. Spacings arrive in pixels and the characters with Figma's text case applied.
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
}

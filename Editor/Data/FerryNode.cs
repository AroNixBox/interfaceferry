using System;

namespace InterfaceFerry
{
    [Serializable]
    class FerryNode
    {
        public string id;
        public string name;
        public string type;
        public int parentIndex;
        public float width;
        public float height;
        // Figma's absoluteTransform, row-major: [m00, m01, m02, m10, m11, m12].
        public float[] t;
        // Containers and text only. Any other leaf's own opacity is baked into its PNG.
        public float opacity = 1f;
        public bool clip;
        public bool onClick;
        public FerryImage image;
        public FerryText text;
    }
}

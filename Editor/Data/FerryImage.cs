using System;

namespace InterfaceFerry
{
    [Serializable]
    class FerryImage
    {
        public string key;
        // The render bounds in the node's own, unrotated space: effects and strokes can reach outside the box.
        public float x;
        public float y;
        public float w;
        public float h;
    }
}

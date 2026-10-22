using System;

namespace InterfaceFerry
{
    /// <summary>
    /// The wire payload, one per pushed selection root. Field names match the plugin's code.js.
    /// </summary>
    [Serializable]
    class FerryPayload
    {
        public int schema;
        public string fileName;
        public float scale;
        public FerryNode[] nodes;
    }
}

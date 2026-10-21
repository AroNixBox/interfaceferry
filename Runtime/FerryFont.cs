using Unity.Properties;
using UnityEngine;

namespace InterfaceFerry
{
    [GeneratePropertyBag]
    public class FerryFont : ScriptableObject
    {
        [HideInInspector] public string figmaName;

        [CreateProperty]
        public Font font;
    }
}

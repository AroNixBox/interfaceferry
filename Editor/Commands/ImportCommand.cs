using System;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace InterfaceFerry.Editor.Commands
{
    static class ImportCommand
    {
        [CliCommand("ferry_import", "Build a uGUI scene under Assets/InterfaceFerry from an InterfaceFerry payload.")]
        public static object Import(
            [CliArg("payload", "The raster payload, as a JSON object.", Required = true)] JObject payload)
        {
            if (payload == null)
            {
                throw new ArgumentException("payload is required");
            }

            var data = payload.ToObject<FerryPayload>();
            if (data == null || data.nodes == null || data.nodes.Length == 0)
            {
                throw new ArgumentException("payload has no nodes");
            }

            if (data.schema != CommandReferences.Schema)
            {
                throw new NotSupportedException(data.schema + " is not supported");
            }

            return FerryImporter.Import(data);
        }
    }
}
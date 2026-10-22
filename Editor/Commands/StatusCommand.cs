using Unity.Pipeline.Commands;

namespace InterfaceFerry.Editor.Commands
{
    public class StatusCommand
    {
        [CliCommand("ferry_status", "Identify this Editor to the InterfaceFerry Figma plugin.")]
        public static object Status()
        {
            return new { interfaceFerry = true, schemas = new[] { CommandReferences.Schema }, project = CommandReferences.ProjectName };
        }
    }
}
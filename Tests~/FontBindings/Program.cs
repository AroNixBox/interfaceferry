using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using InterfaceFerry;

static class Program
{
    static void Main(string[] arguments)
    {
        var assetName = typeof(FerryFontAssets).GetMethod("AssetName", BindingFlags.Static | BindingFlags.NonPublic);
        Check(assetName != null, "Font assets need readable names instead of an underscore for the family/style separator.");
        Check((string)assetName.Invoke(null, new object[] { "Hanken Grotesk / Medium" }) == "Hanken Grotesk - Medium", "Font names must separate family and style with a readable hyphen.");
        Check((string)assetName.Invoke(null, new object[] { "Font:Name / SemiBold" }) == "Font_Name - SemiBold", "Other unsafe filename characters must still be sanitized.");
        var fontUri = "project://database/Assets/InterfaceFerry/Fonts/Hanken.asset?fileID=11400000&guid=abc&type=2#Hanken";
        var fonts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hanken Grotesk / SemiBold"] = fontUri
        };
        var nodes = new[]
        {
            new FerryNode { id = "1:2", name = "Heading & caption", width = 100, height = 20,
                text = new FerryText { family = "Hanken Grotesk", style = "SemiBold", weight = 600,
                    characters = "Hello <world>\nNext line", autoResize = "WIDTH_AND_HEIGHT" } }
        };
        var uxml = new StringBuilder();
        var uss = new StringBuilder();
        var emit = typeof(FerryImporter).GetMethod("Emit", BindingFlags.NonPublic | BindingFlags.Static);
        try
        {
            emit.Invoke(null, new object[] { 0, nodes, new[] { new List<int>() }, "Images/", uxml, uss, 1, fonts });
        }
        catch (ArgumentException)
        {
            throw new Exception("FAIL: the emitter still requires a per-file font map instead of shared font asset references.");
        }
        var label = XElement.Parse("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" + uxml + "</ui:UXML>").Elements().Single();
        var binding = label.Descendants().Single(e => e.Name.LocalName == "DataBinding");
        Check((string)binding.Attribute("data-source") == fontUri, "Binding must directly reference the font asset.");
        Check((string)binding.Attribute("data-source-path") == "font", "Binding must use the font field.");
        Check((string)binding.Attribute("property") == "style.unityFontDefinition", "Binding must override an inherited theme font definition.");
        Check(binding.Attribute("source-to-ui-converters") == null, "Font bindings must directly map the font without a custom converter.");
        Check((string)binding.Attribute("binding-mode") == "ToTarget", "Binding must be one-way to the UI.");
        Check((string)label.Attribute("text") == "Hello <world>\nNext line", "Text and asset URL XML escaping must round-trip.");
        Check(!uss.ToString().Contains("-unity-font-definition:"), "USS must not bake in the assigned font.");
        Check(uss.ToString().Contains("-unity-font-style: normal;"), "Static font styles must not be synthesized again.");
        nodes[0].text.family = "hanken grotesk";
        uxml.Clear();
        emit.Invoke(null, new object[] { 0, nodes, new[] { new List<int>() }, "Images/", uxml, new StringBuilder(), 1, fonts });
        Check(uxml.ToString().Contains("data-source-path=\"font\""), "Case variants must use the same font reference.");

        Console.WriteLine("PASS: readable font names, direct mapping without custom converter, XML escaping, static style, case-insensitive identity.");

        if (arguments.Contains("--existing-project"))
        {
            var count = 0;
            foreach (var screen in Directory.GetFiles("Assets/InterfaceFerry", "*.raster.uxml", SearchOption.AllDirectories))
                foreach (var fontBinding in XDocument.Load(screen).Descendants().Where(e =>
                    e.Name.LocalName == "DataBinding" && (string)e.Attribute("data-source-path") == "font"))
                {
                    Check(fontBinding.Attribute("source-to-ui-converters") == null, "Existing screens must not reference the removed converter.");
                    var source = new Uri((string)fontBinding.Attribute("data-source"));
                    var sourcePath = Uri.UnescapeDataString(source.AbsolutePath).TrimStart('/');
                    var fontFolder = Path.GetDirectoryName(screen).Replace('\\', '/') + "/Fonts/";
                    Check(sourcePath.StartsWith(fontFolder, StringComparison.Ordinal), "Font assets must belong to the screen's Figma project, alongside Images.");
                    Check(File.Exists(sourcePath), "The referenced project font asset must exist after relocation.");
                    count++;
                }
            Console.WriteLine("PASS: existing project: " + count + " direct font binding(s) without custom converters.");
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }
}

using System.Collections.Generic;
using System.IO;

namespace Ryujinx.HLE.Debugger
{
    static class RegisterInformation
    {
        public static readonly Dictionary<string, string> Features = new()
        {
            { "target64.xml", GetEmbeddedResourceContent("target64.xml") },
            { "target32.xml", GetEmbeddedResourceContent("target32.xml") },
            { "aarch64-core.xml", GetEmbeddedResourceContent("aarch64-core.xml") },
            { "aarch64-fpu.xml", GetEmbeddedResourceContent("aarch64-fpu.xml") },
            { "arm-core.xml", GetEmbeddedResourceContent("arm-core.xml") },
            { "arm-neon.xml", GetEmbeddedResourceContent("arm-neon.xml") },
        };

        private static string GetEmbeddedResourceContent(string resourceName)
        {
            using Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Ryujinx.HLE.Debugger.Gdb.Xml." + resourceName);
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}

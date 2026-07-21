using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Ryujinx.UI.LocaleGenerator
{
    [Generator]
    public class LocaleGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<AdditionalText> localeFiles = context.AdditionalTextsProvider.Where(static x => Path.GetDirectoryName(x.Path)?.Replace('\\', '/').EndsWith("assets/Locales") ?? false);

            IncrementalValueProvider<ImmutableArray<(string, string)>> collectedContents = localeFiles.Select((text, cancellationToken) => (text.GetText(cancellationToken)!.ToString(), Path.GetFileName(text.Path))).Collect();

            context.RegisterSourceOutput(collectedContents, (spc, contents) =>
            {
                StringBuilder enumSourceBuilder = new();
                enumSourceBuilder.AppendLine("namespace Ryujinx.Ava.Common.Locale;");
                enumSourceBuilder.AppendLine("public enum LocaleKeys");
                enumSourceBuilder.AppendLine("{");
                
                foreach ((string, string) content in contents)
                {
                    IEnumerable<string> lines = content.Item1.Split('\n').Where(x => x.Trim().StartsWith("\"ID\":")).Select(x => x.Split(':')[1].Trim().Replace("\"", string.Empty).Replace(",", string.Empty));
                    
                    foreach (string? line in lines)
                    {
                        if (content.Item2 == "Root.json")
                        {
                            enumSourceBuilder.AppendLine($"    {line},");
                        }
                        else
                        {
                            enumSourceBuilder.AppendLine($"    {content.Item2.Split('.')[0]}_{line},");
                        }
                    }
                }
                
                enumSourceBuilder.AppendLine("}");

                spc.AddSource("LocaleKeys", enumSourceBuilder.ToString());
            });
        }
    }
}

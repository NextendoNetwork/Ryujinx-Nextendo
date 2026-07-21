using LibHac.Loader;
using Ryujinx.Common;

namespace Ryujinx.HLE.Loaders.Processes.Extensions
{
    public static class MetaLoaderExtensions
    {
        public static void LoadDefault(this MetaLoader metaLoader)
        {
            byte[] npdmBuffer = EmbeddedResources.Read("Ryujinx.HLE/Homebrew.npdm");

            metaLoader.Load(npdmBuffer).ThrowIfFailure();
        }
    }
}

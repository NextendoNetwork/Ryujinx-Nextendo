using System;

namespace Ryujinx.Graphics.GAL
{
    /// <summary>
    /// Array of textures and samplers bound as a whole.
    /// </summary>
    /// <remarks>
    /// The arrays passed to the Set methods are handed over: a threaded renderer keeps them until the
    /// command ran, so the caller must not reuse them.
    /// </remarks>
    public interface ITextureArray : IDisposable
    {
        void SetSamplers(int index, ISampler[] samplers);
        void SetTextures(int index, ITexture[] textures);
    }
}

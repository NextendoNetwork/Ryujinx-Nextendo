using System;

namespace Ryujinx.Graphics.GAL
{
    /// <summary>
    /// Array of images bound as a whole.
    /// </summary>
    /// <remarks>
    /// The array passed to <see cref="SetImages"/> is handed over: a threaded renderer keeps it until the
    /// command ran, so the caller must not reuse it.
    /// </remarks>
    public interface IImageArray : IDisposable
    {
        void SetImages(int index, ITexture[] images);
    }
}

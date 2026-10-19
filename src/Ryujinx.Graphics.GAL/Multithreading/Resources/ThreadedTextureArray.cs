using Ryujinx.Graphics.GAL.Multithreading.Commands.TextureArray;
using Ryujinx.Graphics.GAL.Multithreading.Model;

namespace Ryujinx.Graphics.GAL.Multithreading.Resources
{
    /// <summary>
    /// Threaded representation of a texture and sampler array.
    /// </summary>
    class ThreadedTextureArray : ITextureArray
    {
        private readonly ThreadedRenderer _renderer;
        public ITextureArray Base;

        public ThreadedTextureArray(ThreadedRenderer renderer)
        {
            _renderer = renderer;
        }

        private TableRef<T> Ref<T>(T reference)
        {
            return new TableRef<T>(_renderer, reference);
        }

        public unsafe void Dispose()
        {
            _renderer.New<TextureArrayDisposeCommand>()->Set(Ref(this));
            _renderer.QueueCommand();
        }

        public unsafe void SetSamplers(int index, ISampler[] samplers)
        {
            // The array is handed over (see ITextureArray), so it is not copied.
            _renderer.New<TextureArraySetSamplersCommand>()->Set(Ref(this), index, Ref(samplers));
            _renderer.QueueCommand();
        }

        public unsafe void SetTextures(int index, ITexture[] textures)
        {
            _renderer.New<TextureArraySetTexturesCommand>()->Set(Ref(this), index, Ref(textures));
            _renderer.QueueCommand();
        }
    }
}

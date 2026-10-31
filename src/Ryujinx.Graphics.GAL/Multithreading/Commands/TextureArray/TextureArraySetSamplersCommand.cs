using Ryujinx.Graphics.GAL.Multithreading.Model;
using Ryujinx.Graphics.GAL.Multithreading.Resources;

namespace Ryujinx.Graphics.GAL.Multithreading.Commands.TextureArray
{
    struct TextureArraySetSamplersCommand : IGALCommand, IGALCommand<TextureArraySetSamplersCommand>
    {
        public readonly CommandType CommandType => CommandType.TextureArraySetSamplers;
        private TableRef<ThreadedTextureArray> _textureArray;
        private int _index;
        private TableRef<ISampler[]> _samplers;

        public void Set(TableRef<ThreadedTextureArray> textureArray, int index, TableRef<ISampler[]> samplers)
        {
            _textureArray = textureArray;
            _index = index;
            _samplers = samplers;
        }

        public static void Run(ref TextureArraySetSamplersCommand command, ThreadedRenderer threaded, IRenderer renderer)
        {
            ThreadedTextureArray textureArray = command._textureArray.Get(threaded);
            ISampler[] array = command._samplers.Get(threaded);

            // The array was handed over, so the proxies are replaced in place.
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = ((ThreadedSampler)array[i])?.Base;
            }

            textureArray.Base.SetSamplers(command._index, array);
        }
    }
}

using Ryujinx.Graphics.GAL.Multithreading.Model;
using Ryujinx.Graphics.GAL.Multithreading.Resources;

namespace Ryujinx.Graphics.GAL.Multithreading.Commands.TextureArray
{
    struct TextureArraySetTexturesCommand : IGALCommand, IGALCommand<TextureArraySetTexturesCommand>
    {
        public readonly CommandType CommandType => CommandType.TextureArraySetTextures;
        private TableRef<ThreadedTextureArray> _textureArray;
        private int _index;
        private TableRef<ITexture[]> _textures;

        public void Set(TableRef<ThreadedTextureArray> textureArray, int index, TableRef<ITexture[]> textures)
        {
            _textureArray = textureArray;
            _index = index;
            _textures = textures;
        }

        public static void Run(ref TextureArraySetTexturesCommand command, ThreadedRenderer threaded, IRenderer renderer)
        {
            ThreadedTextureArray textureArray = command._textureArray.Get(threaded);
            ITexture[] array = command._textures.Get(threaded);

            // The array was handed over, so the proxies are replaced in place.
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = ((ThreadedTexture)array[i])?.Base;
            }

            textureArray.Base.SetTextures(command._index, array);
        }
    }
}

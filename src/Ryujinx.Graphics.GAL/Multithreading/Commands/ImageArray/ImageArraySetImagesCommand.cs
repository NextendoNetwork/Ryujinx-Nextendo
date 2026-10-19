using Ryujinx.Graphics.GAL.Multithreading.Model;
using Ryujinx.Graphics.GAL.Multithreading.Resources;

namespace Ryujinx.Graphics.GAL.Multithreading.Commands.ImageArray
{
    struct ImageArraySetImagesCommand : IGALCommand, IGALCommand<ImageArraySetImagesCommand>
    {
        public readonly CommandType CommandType => CommandType.ImageArraySetImages;
        private TableRef<ThreadedImageArray> _imageArray;
        private int _index;
        private TableRef<ITexture[]> _images;

        public void Set(TableRef<ThreadedImageArray> imageArray, int index, TableRef<ITexture[]> images)
        {
            _imageArray = imageArray;
            _index = index;
            _images = images;
        }

        public static void Run(ref ImageArraySetImagesCommand command, ThreadedRenderer threaded, IRenderer renderer)
        {
            ThreadedImageArray imageArray = command._imageArray.Get(threaded);
            ITexture[] array = command._images.Get(threaded);

            // The array was handed over, so the proxies are replaced in place.
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = ((ThreadedTexture)array[i])?.Base;
            }

            imageArray.Base.SetImages(command._index, array);
        }
    }
}

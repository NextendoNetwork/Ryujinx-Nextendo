using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.ViewModels;
using System.Reflection;

namespace Ryujinx.Ava.UI.Controls
{
    public class RyujinxLogo : Image
    {
        // The UI specifically uses a thicker bordered variant of the icon to avoid crunching out the border at lower resolutions.
        // For an example of this, download canary 1.2.95, then open the settings menu, and look at the icon in the top-left.
        // The border gets reduced to colored pixels in the 4 corners.
        public static readonly Bitmap Bitmap =
            new(Assembly.GetAssembly(typeof(MainWindowViewModel))!
                .GetManifestResourceStream("Ryujinx.Assets.UIImages.Logo_Nextendo.png")!);

        public RyujinxLogo()
        {
            Margin = new Thickness(7, 7, 7, 0);
            Height = 25;
            Width = 25;
            Source = Bitmap;
            Stretch = Stretch.Uniform;
            // [Nextendo] The source is a 1600x1600 PNG: without this the tiny 25x25 display
            // falls back to nearest-neighbour and the logo looks crunchy at the edges.
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
            IsVisible = !ConfigurationState.Instance.ShowOldUI;
        }
    }
}

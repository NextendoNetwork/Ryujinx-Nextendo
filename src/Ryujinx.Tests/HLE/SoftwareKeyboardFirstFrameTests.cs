using NUnit.Framework;
using Ryujinx.HLE.HOS.Applets.SoftwareKeyboard;
using Ryujinx.HLE.HOS.Services.SurfaceFlinger;
using Ryujinx.HLE.UI;
using Ryujinx.Memory;
using Ryujinx.Tests.Memory;
using System;

namespace Ryujinx.Tests.HLE
{
    public class SoftwareKeyboardFirstFrameTests
    {
        private sealed class Theme : IHostUITheme
        {
            public string FontFamily => "Arial";
            public ThemeColor DefaultBackgroundColor => new(1, 0, 0, 0);
            public ThemeColor DefaultForegroundColor => new(1, 1, 1, 1);
            public ThemeColor DefaultBorderColor => new(1, 1, 1, 1);
            public ThemeColor SelectionBackgroundColor => new(1, 0, 0, 1);
            public ThemeColor SelectionForegroundColor => new(1, 1, 1, 1);
        }

        private sealed class RecordingMemory : MockVirtualMemoryManager, IVirtualMemoryManager
        {
            public byte[] Image;
            public RecordingMemory() : base(0x400000, 0x1000) { }
            void IVirtualMemoryManager.Write(ulong address, ReadOnlySpan<byte> data) => Image = data.ToArray();
        }

        [Test]
        public void FirstIndirectLayerRequestHasAnImageWithoutWaitingForBlink()
        {
            const uint width = 1280, height = 768, pitch = width * 4, size = pitch * height;
            using SoftwareKeyboardRenderer renderer = new(new Theme());
            RecordingMemory memory = new();
            renderer.SetSurfaceInfo(new RenderingSurfaceInfo(ColorFormat.A8B8G8R8, width, height, pitch, size));
            Assert.That(renderer.DrawTo(memory, 0), Is.True);
            Assert.That(memory.Image.Length, Is.EqualTo(size));
            Assert.That(Array.Exists(memory.Image, value => value != 0), Is.True);
            renderer.UpdateTextState("1234", 4, 4, false, true);
            renderer.SetSurfaceInfo(new RenderingSurfaceInfo(ColorFormat.A8B8G8R8, width, height, pitch, size));
            Assert.That(renderer.DrawTo(memory, 0), Is.True);
        }
    }
}

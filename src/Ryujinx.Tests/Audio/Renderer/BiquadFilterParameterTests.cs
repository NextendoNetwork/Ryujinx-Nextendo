using NUnit.Framework;
using Ryujinx.Audio.Renderer.Parameter;
using System.Runtime.CompilerServices;

namespace Ryujinx.Tests.Audio.Renderer
{
    class BiquadFilterParameterTests
    {
        [Test]
        public void EnsureTypeSize()
        {
            Assert.AreEqual(0xC, Unsafe.SizeOf<BiquadFilterParameter1>());
            Assert.AreEqual(0x18, Unsafe.SizeOf<BiquadFilterParameter2>());
        }
    }
}

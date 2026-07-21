using NUnit.Framework;
using Ryujinx.Audio.Renderer.Server.MemoryPool;
using System.Runtime.CompilerServices;

namespace Ryujinx.Tests.Audio.Renderer.Server
{
    class MemoryPoolInfoTests
    {
        [Test]
        public void EnsureTypeSize()
        {
            Assert.AreEqual(Unsafe.SizeOf<MemoryPoolInfo>(), 0x20);
        }

        [Test]
        public void TestContains()
        {
            MemoryPoolInfo memoryPool = MemoryPoolInfo.Create(MemoryPoolInfo.LocationType.Cpu);

            memoryPool.SetCpuAddress(0x1000000, 0x1000);

            memoryPool.DspAddress = 0x2000000;

            Assert.IsTrue(memoryPool.Contains(0x1000000, 0x10));
            Assert.IsTrue(memoryPool.Contains(0x1000FE0, 0x10));
            Assert.IsTrue(memoryPool.Contains(0x1000FFF, 0x1));
            Assert.IsFalse(memoryPool.Contains(0x1000FFF, 0x2));
            Assert.IsFalse(memoryPool.Contains(0x1001000, 0x10));
            Assert.IsFalse(memoryPool.Contains(0x2000000, 0x10));
        }

        [Test]
        public void TestTranslate()
        {
            MemoryPoolInfo memoryPool = MemoryPoolInfo.Create(MemoryPoolInfo.LocationType.Cpu);

            memoryPool.SetCpuAddress(0x1000000, 0x1000);

            memoryPool.DspAddress = 0x2000000;

            Assert.AreEqual(0x2000FE0, memoryPool.Translate(0x1000FE0, 0x10));
            Assert.AreEqual(0x2000FFF, memoryPool.Translate(0x1000FFF, 0x1));
            Assert.AreEqual(0x0, memoryPool.Translate(0x1000FFF, 0x2));
            Assert.AreEqual(0x0, memoryPool.Translate(0x1001000, 0x10));
            Assert.AreEqual(0x0, memoryPool.Translate(0x2000000, 0x10));
        }

        [Test]
        public void TestIsMapped()
        {
            MemoryPoolInfo memoryPool = MemoryPoolInfo.Create(MemoryPoolInfo.LocationType.Cpu);

            memoryPool.SetCpuAddress(0x1000000, 0x1000);

            Assert.IsFalse(memoryPool.IsMapped());

            memoryPool.DspAddress = 0x2000000;

            Assert.IsTrue(memoryPool.IsMapped());
        }
    }
}

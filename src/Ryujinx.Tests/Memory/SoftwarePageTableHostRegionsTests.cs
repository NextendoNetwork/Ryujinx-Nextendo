using NUnit.Framework;
using Ryujinx.Cpu.Jit;
using Ryujinx.Memory;
using Ryujinx.Memory.Range;
using System.Linq;

namespace Ryujinx.Tests.Memory
{
    [TestFixture]
    internal class SoftwarePageTableHostRegionsTests
    {
        private const ulong PageSize = 0x1000;

        [Test]
        public void GetHostRegions_UnalignedRange_StartsAndEndsAtRequestedAddress()
        {
            using MemoryBlock ram = new(PageSize * 4);
            MemoryManager memory = new(ram, 1UL << 32);
            memory.IncrementReferenceCount();

            const ulong Va = 0x10000;

            // Two virtual pages backed by physical pages 2 and 3, so the mapping is contiguous.
            memory.Map(Va, PageSize * 2, PageSize * 2, MemoryMapFlags.Private);

            HostMemoryRange[] regions = memory.GetHostRegions(Va + 0x800, 0x1000).ToArray();

            Assert.That(regions, Has.Length.EqualTo(1));
            Assert.That(regions[0].Address, Is.EqualTo((nuint)(ulong)ram.GetPointer(PageSize * 2 + 0x800, 0x1000)));
            Assert.That(regions[0].Size, Is.EqualTo(0x1000UL));

            memory.DecrementReferenceCount();
        }
    }
}

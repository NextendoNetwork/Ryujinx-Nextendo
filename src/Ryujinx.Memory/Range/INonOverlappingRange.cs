namespace Ryujinx.Memory.Range
{
    /// <summary>
    /// Range of memory that can be split in two.
    /// </summary>
    public interface INonOverlappingRange<T> : IRangeListRange<T> where T : class, IRangeListRange<T>
    {
        /// <summary>
        /// Split this region into two, around the specified address. 
        /// This region is updated to end at the split address, and a new region is created to represent past that point.
        /// </summary>
        /// <param name="splitAddress">Address to split the region around</param>
        /// <returns>The second part of the split region, with start address at the given split.</returns>
        public INonOverlappingRange<T> Split(ulong splitAddress);
    }
}

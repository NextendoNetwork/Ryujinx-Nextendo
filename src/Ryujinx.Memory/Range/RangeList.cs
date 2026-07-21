using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ryujinx.Memory.Range
{
    /// <summary>
    /// Result of an Overlaps Finder function. WARNING: if the result is from the optimized
    /// Overlaps Finder, the StartIndex will be -1 even when the result isn't empty
    /// </summary>
    /// <remarks>
    /// startIndex is inclusive.
    /// endIndex is exclusive.
    /// </remarks>
    public readonly struct OverlapResult<T> where T : class, IRangeListRange<T>
    {
        public readonly int StartIndex = -1;
        public readonly int EndIndex = -1;
        public readonly T QuickResult;
        public int Count => EndIndex - StartIndex;

        public OverlapResult(int startIndex, int endIndex, T quickResult = null)
        {
            this.StartIndex = startIndex;
            this.EndIndex = endIndex;
            this.QuickResult = quickResult;
        }
    }

    /// <summary>
    /// Sorted list of ranges that supports binary search.
    /// </summary>
    /// <typeparam name="T">Type of the range.</typeparam>
    public class RangeList<T> : RangeListBase<T> where T : class, IRangeListRange<T>
    {
        public readonly ReaderWriterLockSlim Lock = new();

        /// <summary>
        /// Creates a new range list.
        /// </summary>
        public RangeList() { }
        
        /// <summary>
        /// Creates a new range list.
        /// </summary>
        /// <param name="backingInitialSize">The initial size of the backing array</param>
        public RangeList(int backingInitialSize) : base(backingInitialSize) { }

        /// <summary>
        /// Adds a new item to the list.
        /// </summary>
        /// <param name="item">The item to be added</param>
        public override void Add(T item)
        {
            int index = BinarySearch(item.Address);

            if (index < 0)
            {
                index = ~index;
            }

            if (Count + 1 > Items.Length)
            {
                Array.Resize(ref Items, Items.Length + BackingGrowthSize);
            }

            if (index >= Count)
            {
                if (index == Count)
                {
                    if (index != 0)
                    {
                        item.Previous = Items[index - 1];
                        Items[index - 1].Next = item;
                    }
                    Items[index] = item;
                    Count++;
                }
            }
            else
            {
                Array.Copy(Items, index, Items, index + 1, Count - index);

                Items[index] = item;
                if (index != 0)
                {
                    item.Previous = Items[index - 1];
                    Items[index - 1].Next = item;
                }
                
                item.Next = Items[index + 1];
                Items[index + 1].Previous = item;
                
                Count++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveAt(int index)
        {
            if (index < Count - 1)
            {
                Items[index + 1].Previous = index > 0 ? Items[index - 1] : null;
            }

            if (index > 0)
            {
                Items[index - 1].Next = index < Count - 1 ? Items[index + 1] : null;
            }
            
            if (index < --Count)
            {
                Array.Copy(Items, index + 1, Items, index, Count - index);
            }
        }

        /// <summary>
        /// Removes a range of items from the item list
        /// </summary>
        /// <param name="startItem">The first item in the range of items to be removed</param>
        /// <param name="endItem">The last item in the range of items to be removed</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void RemoveRange(T startItem, T endItem)
        {
            if (startItem is null)
            {
                return;
            }

            if (startItem == endItem)
            {
                Remove(startItem);
                return;
            }
            
            (int index, int endIndex) = BinarySearchEdges(startItem.Address, endItem.EndAddress);
            
            if (endIndex < Count)
            {
                Items[endIndex].Previous = index > 0 ? Items[index - 1] : null;
            }

            if (index > 0)
            {
                Items[index - 1].Next = endIndex < Count ? Items[endIndex] : null;
            }
            
            
            if (endIndex < Count)
            {
                Array.Copy(Items, endIndex, Items, index, Count - endIndex);
            }
            
            Count -= endIndex - index;
        }

        /// <summary>
        /// Removes an item from the list.
        /// </summary>
        /// <param name="item">The item to be removed</param>
        /// <returns>True if the item was removed, or false if it was not found</returns>
        public override bool Remove(T item)
        {
            int index = BinarySearch(item.Address);

            if (index >= 0)
            {
                while (index < Count)
                {
                    if (Items[index] == item)
                    {
                        RemoveAt(index);

                        return true;
                    }

                    if (Items[index].Address > item.Address)
                    {
                        break;
                    }

                    index++;
                }
            }

            return false;
        }
        
        /// <summary>
        /// Gets an item on the list overlapping the specified memory range.
        /// </summary>
        /// <remarks>
        /// This has no ordering guarantees of the returned item.
        /// It only ensures that the item returned overlaps the specified memory range.
        /// </remarks>
        /// <param name="address">Start address of the range</param>
        /// <param name="size">Size in bytes of the range</param>
        /// <returns>The overlapping item, or the default value for the type if none found</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override T FindOverlap(ulong address, ulong size)
        {
            int index = BinarySearchLeftEdge(address, address + size);

            if (index < 0)
            {
                return null;
            }

            return Items[index];
        }

        /// <summary>
        /// Gets an item on the list overlapping the specified memory range.
        /// </summary>
        /// <remarks>
        /// This has no ordering guarantees of the returned item.
        /// It only ensures that the item returned overlaps the specified memory range.
        /// </remarks>
        /// <param name="address">Start address of the range</param>
        /// <param name="size">Size in bytes of the range</param>
        /// <returns>The overlapping item, or the default value for the type if none found</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override T FindOverlapFast(ulong address, ulong size)
        {
            int index = BinarySearch(address, address + size);

            if (index < 0)
            {
                return null;
            }

            return Items[index];
        }
        
        /// <summary>
        /// Gets all items on the list overlapping the specified memory range.
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="size">Size in bytes of the range</param>
        /// <param name="output">Output array where matches will be written. It is automatically resized to fit the results</param>
        /// <returns>Range information of overlapping items found</returns>
        private OverlapResult<T> FindOverlaps(ulong address, ulong size, ref T[] output)
        {
            int outputCount = 0;

            ulong endAddress = address + size;
            
            int startIndex = BinarySearch(address, endAddress);
            if (startIndex < 0)
                startIndex = ~startIndex;
            int endIndex = -1;

            for (int i = startIndex; i < Count; i++)
            {
                T item = Items[i];

                if (item.Address >= endAddress)
                {
                    endIndex = i;
                    break;
                }

                if (item.OverlapsWith(address, endAddress))
                {
                    outputCount++;
                }
            }

            if (endIndex == -1 && outputCount > 0)
            {
                endIndex = Count;
            }

            if (outputCount > 0 && outputCount == endIndex - startIndex)
            {
                Array.Resize(ref output, outputCount);
                Array.Copy(Items, endIndex - outputCount, output, 0, outputCount);
                
                return new OverlapResult<T>(startIndex, endIndex);
            }
            else if (outputCount > 0)
            {
                Array.Resize(ref output, outputCount);
                int arrIndex = 0;
                for (int i = startIndex; i < endIndex; i++)
                {
                    output[arrIndex++] = Items[i];
                }
                
                return new OverlapResult<T>(endIndex - outputCount, endIndex);
            }
            
            return new OverlapResult<T>();
        }

        public override IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return Items[i];
            }
        }
    }
}

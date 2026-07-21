using Ryujinx.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ryujinx.Memory.Range
{
    public interface IRangeListRange<TValue> : IRange where TValue : class, IRangeListRange<TValue>
    {
        public TValue Next { get; set; }
        public TValue Previous { get; set; }
    }
    
    public unsafe abstract class RangeListBase<T> : IEnumerable<T> where T : class, IRangeListRange<T>
    {
        private const int BackingInitialSize = 1024;

        protected T[] Items;
        protected readonly int BackingGrowthSize;
        
        public int Count { get; protected set; }
        
        /// <summary>
        /// Creates a new range list.
        /// </summary>
        /// <param name="backingInitialSize">The initial size of the backing array</param>
        protected RangeListBase(int backingInitialSize = BackingInitialSize)
        {
            BackingGrowthSize = backingInitialSize;
            Items = new T[backingInitialSize];
        }
        
        public abstract void Add(T item);
        
        public abstract bool Remove(T item);

        public abstract void RemoveRange(T startItem, T endItem);

        public abstract T FindOverlap(ulong address, ulong size);
        
        public abstract T FindOverlapFast(ulong address, ulong size);

        /// <summary>
        /// Performs binary search on the internal list of items.
        /// </summary>
        /// <param name="address">Address to find</param>
        /// <returns>List index of the item, or complement index of nearest item with lower value on the list</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected int BinarySearch(ulong address)
        {
            int left = 0;
            int right = Count - 1;

            while (left <= right)
            {
                int range = right - left;

                int middle = left + (range >> 1);

                T item = Items[middle];

                if (item.Address == address)
                {
                    return middle;
                }

                if (address < item.Address)
                {
                    right = middle - 1;
                }
                else
                {
                    left = middle + 1;
                }
            }

            return ~left;
        }
        
        /// <summary>
        /// Performs binary search for items overlapping a given memory range.
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="endAddress">End address of the range</param>
        /// <returns>List index of the item, or complement index of nearest item with lower value on the list</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected int BinarySearch(ulong address, ulong endAddress)
        {
            int left = 0;
            int right = Count - 1;

            while (left <= right)
            {
                int range = right - left;

                int middle = left + (range >> 1);

                T item = Items[middle];

                if (item.OverlapsWith(address, endAddress))
                {
                    return middle;
                }

                if (address < item.Address)
                {
                    right = middle - 1;
                }
                else
                {
                    left = middle + 1;
                }
            }

            return ~left;
        }
        
        /// <summary>
        /// Performs binary search for items overlapping a given memory range.
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="endAddress">End address of the range</param>
        /// <returns>List index of the item, or complement index of nearest item with lower value on the list</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected int BinarySearchLeftEdge(ulong address, ulong endAddress)
        {
            if (Count == 0)
                return ~0;

            int left = 0;
            int right = Count - 1;

            while (left <= right)
            {
                int range = right - left;

                int middle = left + (range >> 1);

                T item = Items[middle];

                bool match = item.OverlapsWith(address, endAddress);

                if (range == 0)
                {
                    if (match)
                        return middle;
                    else if (address < item.Address)
                        return ~(right);
                    else
                        return ~(right + 1);
                }

                if (match)
                {
                    right = middle;
                }
                else if (address < item.Address)
                {
                    right = middle - 1;
                }
                else
                {
                    left = middle + 1;
                }
            }

            return ~left;
        }
        
        /// <summary>
        /// Performs binary search for items overlapping a given memory range.
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="endAddress">End address of the range</param>
        /// <returns>List index of the item, or complement index of nearest item with lower value on the list</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected int BinarySearchRightEdge(ulong address, ulong endAddress)
        {
            if (Count == 0)
                return ~0;

            int left = 0;
            int right = Count - 1;

            while (left <= right)
            {
                int range = right - left;

                int middle = right - (range >> 1);

                T item = Items[middle];

                bool match = item.OverlapsWith(address, endAddress);

                if (range == 0)
                {
                    if (match)
                        return middle;
                    else if (endAddress > item.EndAddress)
                        return ~(left + 1);
                    else
                        return ~(left);
                }

                if (match)
                {
                    left = middle;
                }
                else if (address < item.Address)
                {
                    right = middle - 1;
                }
                else
                {
                    left = middle + 1;
                }
            }

            return ~left;
        }
        
        /// <summary>
        /// Performs binary search for items overlapping a given memory range.
        /// </summary>
        /// <param name="address">Start address of the range</param>
        /// <param name="endAddress">End address of the range</param>
        /// <returns>Range information (inclusive, exclusive) of items that overlaps, or complement index of nearest item with lower value on the list</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected (int, int) BinarySearchEdges(ulong address, ulong endAddress)
        {
            if (Count == 0)
                return (~0, ~0);

            if (Count == 1)
            {
                T item = Items[0];

                if (item.OverlapsWith(address, endAddress))
                {
                    return (0, 1);
                }

                if (address < item.Address)
                {
                    return (~0, ~0);
                }
                else
                {
                    return (~1, ~1);
                }
            }

            int left = 0;
            int right = Count - 1;

            int leftEdge = -1;
            int rightEdgeMatch = -1;
            int rightEdgeNoMatch = -1;

            while (left <= right)
            {
                int range = right - left;

                int middle = left + (range >> 1);

                T item = Items[middle];

                bool match = item.OverlapsWith(address, endAddress);

                if (range == 0)
                {
                    if (match)
                    {
                        leftEdge = middle;
                        break;
                    }
                    else if (address < item.Address)
                    {
                        return (~right, ~right);
                    }
                    else
                    {
                        return (~(right + 1), ~(right + 1));
                    }
                }

                if (match)
                {
                    right = middle;
                    if (rightEdgeMatch == -1)
                        rightEdgeMatch = middle;
                }
                else if (address < item.Address)
                {
                    right = middle - 1;
                    rightEdgeNoMatch = middle;
                }
                else
                {
                    left = middle + 1;
                }
            }

            if (left > right)
            {
                return (~left, ~left);
            }

            if (rightEdgeMatch == -1)
            {
                return (leftEdge, leftEdge + 1);
            }

            left = rightEdgeMatch;
            right = rightEdgeNoMatch > 0 ? rightEdgeNoMatch : Count - 1;

            while (left <= right)
            {
                int range = right - left;

                int middle = right - (range >> 1);

                T item = Items[middle];

                bool match = item.OverlapsWith(address, endAddress);

                if (range == 0)
                {
                    if (match)
                        return (leftEdge, middle + 1);
                    else
                        return (leftEdge, middle);
                }

                if (match)
                {
                    left = middle;
                }
                else if (address < item.Address)
                {
                    right = middle - 1;
                }
                else
                {
                    left = middle + 1;
                }
            }

            return (leftEdge, right + 1);
        }
        
        public abstract IEnumerator<T> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

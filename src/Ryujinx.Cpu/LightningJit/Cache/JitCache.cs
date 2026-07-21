using ARMeilleure.Memory;
using Humanizer;
using Ryujinx.Common.Logging;
using Ryujinx.Memory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Ryujinx.Cpu.LightningJit.Cache
{
    partial class JitCache : IDisposable
    {
        private static readonly int _pageSize = (int)MemoryBlock.GetPageSize();
        private static readonly int _pageMask = _pageSize - 1;

        private const int CodeAlignment = 4; // Bytes.
        private const uint CacheSize = 256 * 1024 * 1024; // Megabytes * Size of Megabytes (since its in bytes).

        private readonly JitCacheInvalidation _jitCacheInvalidator;

        private readonly  CacheMemoryAllocator _cacheAllocator;

        private readonly List<CacheEntry> _cacheEntries = [];

        private readonly Lock _lock = new();
        
        private readonly List<ReservedRegion> _jitRegions = [];

        public JitCache(IJitMemoryAllocator allocator)
        {
            lock (_lock)
            {
                _jitRegions.Add(new(allocator, CacheSize));
                
                _cacheAllocator = new CacheMemoryAllocator((int)CacheSize);
                
                if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                {
                    _jitCacheInvalidator = new JitCacheInvalidation(allocator);
                }
            }
        }

        public nint Map(ReadOnlySpan<byte> code)
        {
            lock (_lock)
            {
                int funcOffset = Allocate(code.Length);
                nint funcPtr = GetFunctionPtr(funcOffset);

                if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    unsafe
                    {
                        fixed (byte* codePtr = code)
                        {
                            JitSupportDarwin.Copy(funcPtr, (nint)codePtr, (ulong)code.Length);
                        }
                    }
                }
                else
                {
                    ReprotectAsWritable(funcOffset, code.Length);
                    Marshal.Copy(code.ToArray(), 0, funcPtr, code.Length);
                    ReprotectAsExecutable(funcOffset, code.Length);

                    _jitCacheInvalidator?.Invalidate(funcPtr, (ulong)code.Length);
                }

                Add(funcOffset, code.Length);

                return funcPtr;
            }
        }

        public void Unmap(nint pointer)
        {
            lock (_lock)
            {
                for (int i = 0; i < _jitRegions.Count; i++)
                {
                    ReservedRegion region = _jitRegions[i];
                    if (pointer.ToInt64() < region.Pointer.ToInt64() ||
                        pointer.ToInt64() >= (region.Pointer + (nint)CacheSize).ToInt64())
                    {
                        continue;
                    }

                    int funcOffset = (int)(pointer.ToInt64() - region.Pointer.ToInt64() + i * CacheSize);

                    if (TryFind(funcOffset, out CacheEntry entry, out int entryIndex) && entry.Offset == funcOffset)
                    {
                        _cacheAllocator.Free(funcOffset, AlignCodeSize(entry.Size));
                        _cacheEntries.RemoveAt(entryIndex);
                    }

                    return;
                }
            }
        }

        private void ReprotectAsWritable(int offset, int size)
        {
            int endOffs = offset + size;
            int regionStart = (offset % (int)CacheSize) & ~_pageMask;
            int regionEnd = endOffs % (int)CacheSize == 0
                ? (((int)CacheSize) + _pageMask) & ~_pageMask
                : ((endOffs % (int)CacheSize) + _pageMask) & ~_pageMask;

            GetRegion(offset).Block.MapAsRwx((ulong)regionStart, (ulong)(regionEnd - regionStart));
        }

        private void ReprotectAsExecutable(int offset, int size)
        {
            int endOffs = offset + size;
            int regionStart = (offset % (int)CacheSize) & ~_pageMask;
            int regionEnd = endOffs % (int)CacheSize == 0
                ? (((int)CacheSize) + _pageMask) & ~_pageMask
                : ((endOffs % (int)CacheSize) + _pageMask) & ~_pageMask;

            GetRegion(offset).Block.MapAsRx((ulong)regionStart, (ulong)(regionEnd - regionStart));
        }

        private int Allocate(int codeSize)
        {
            codeSize = AlignCodeSize(codeSize);
            
            int allocOffset = _cacheAllocator.Allocate(codeSize); 

            if (allocOffset >= 0)
            {
                GetRegion(allocOffset).ExpandIfNeeded((ulong)(allocOffset % (int)CacheSize) + (ulong)codeSize);
                return allocOffset;
            }
            
            _cacheAllocator.AddNewBlocks(1);
            ReservedRegion newRegion = new(_jitRegions[0].Allocator, CacheSize);
            
            Logger.Warning?.Print(LogClass.Cpu, $"JIT Cache of size {(_jitRegions.Count * CacheSize).Bytes()} exhausted, creating new Cache Region ({((_jitRegions.Count + 1) * CacheSize).Bytes()} Total Allocation).");

            _jitRegions.Add(newRegion);

            allocOffset = _cacheAllocator.Allocate(codeSize);
            if (allocOffset < 0)
            {
                throw new OutOfMemoryException("Failed to allocate in new Cache Region!");
            }

            GetRegion(allocOffset).ExpandIfNeeded((ulong)(allocOffset % (int)CacheSize) + (ulong)codeSize);
            return allocOffset;
        }
        
        private nint GetFunctionPtr(int offset)
        {
            return GetRegion(offset).Pointer + (offset % (int)CacheSize);
        }

        private ReservedRegion GetRegion(int offset)
        {
            int index = offset / (int)CacheSize;

            return _jitRegions[index];
        }

        private static int AlignCodeSize(int codeSize)
        {
            return checked(codeSize + (CodeAlignment - 1)) & ~(CodeAlignment - 1);
        }

        private void Add(int offset, int size)
        {
            CacheEntry entry = new(offset, size);

            int index = _cacheEntries.BinarySearch(entry);

            if (index < 0)
            {
                index = ~index;
            }

            _cacheEntries.Insert(index, entry);
        }

        public bool TryFind(int offset, out CacheEntry entry, out int entryIndex)
        {
            lock (_lock)
            {
                int index = _cacheEntries.BinarySearch(new CacheEntry(offset, 0));

                if (index < 0)
                {
                    index = ~index - 1;
                }

                if (index >= 0)
                {
                    entry = _cacheEntries[index];
                    entryIndex = index;
                    return true;
                }
            }

            entry = default;
            entryIndex = 0;
            return false;
        }
        
        public void Dispose()
        {
            foreach (ReservedRegion jitRegion in _jitRegions)
            {
                jitRegion.Dispose();
            }
        }
    }
}

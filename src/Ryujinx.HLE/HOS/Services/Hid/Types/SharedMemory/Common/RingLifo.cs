using Ryujinx.Common.Memory;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.Common
{
    struct RingLifo<T> where T : unmanaged, ISampledDataStruct
    {
        private const ulong MaxEntries = 17;

#pragma warning disable IDE0051, CS0169 // Remove unused private member
        private readonly ulong _unused;
#pragma warning restore IDE0051, CS0169
#pragma warning disable CS0414, IDE0052 // Remove unread private member
        private ulong _bufferCount;
#pragma warning restore CS0414, IDE0052
        private ulong _index;
        private ulong _count;
        private Array17<AtomicStorage<T>> _storage;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong ReadCurrentIndex()
        {
            return Interlocked.Read(ref _index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong ReadCurrentCount()
        {
            return Interlocked.Read(ref _count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly ulong GetNextIndexForWrite(ulong index)
        {
            return (index + 1) % MaxEntries;
        }

        public ref AtomicStorage<T> GetCurrentAtomicEntryRef()
        {
            ulong countAvailable = Math.Min(Math.Max(0, ReadCurrentCount()), 1);

            if (countAvailable == 0)
            {
                _storage[0] = default;

                return ref _storage[0];
            }

            ulong index = ReadCurrentIndex();
            
            Span<AtomicStorage<T>> storageSpan = _storage.AsSpan();

            while (true)
            {
                int inputEntryIndex = (int)((index + MaxEntries + 1 - countAvailable) % MaxEntries);

                ref AtomicStorage<T> result = ref storageSpan[inputEntryIndex];

                ulong samplingNumber0 = result.ReadSamplingNumberAtomic();
                ulong samplingNumber1 = result.ReadSamplingNumberAtomic();

                if (samplingNumber0 != samplingNumber1 && (result.SamplingNumber - result.SamplingNumber) != 1)
                {
                    ulong tempCount = Math.Min(ReadCurrentCount(), countAvailable);

                    countAvailable = Math.Min(tempCount, 1);
                    index = ReadCurrentIndex();

                    continue;
                }

                return ref result;
            }
        }

        public ref T GetCurrentEntryRef()
        {
            return ref GetCurrentAtomicEntryRef().Object;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<AtomicStorage<T>> ReadEntries(uint maxCount)
        {
            ulong countAvailable = Math.Min(Math.Max(0, ReadCurrentCount()), maxCount);

            if (countAvailable == 0)
            {
                return ReadOnlySpan<AtomicStorage<T>>.Empty;
            }

            ulong index = ReadCurrentIndex();

            AtomicStorage<T>[] result = new AtomicStorage<T>[countAvailable];
            
            Span<AtomicStorage<T>> storageSpan = _storage.AsSpan();

            for (ulong i = 0; i < countAvailable; i++)
            {
                int inputEntryIndex = (int)((index + MaxEntries + 1 - countAvailable + i) % MaxEntries);
                int outputEntryIndex = (int)(countAvailable - i - 1);

                ulong samplingNumber0 = storageSpan[inputEntryIndex].ReadSamplingNumberAtomic();
                result[outputEntryIndex] = storageSpan[inputEntryIndex];
                ulong samplingNumber1 = storageSpan[inputEntryIndex].ReadSamplingNumberAtomic();

                if (samplingNumber0 != samplingNumber1 && (i > 0 && (result[outputEntryIndex].SamplingNumber - result[outputEntryIndex].SamplingNumber) != 1))
                {
                    ulong tempCount = Math.Min(ReadCurrentCount(), countAvailable);

                    countAvailable = Math.Min(tempCount, maxCount);
                    index = ReadCurrentIndex();

                    i -= 1;
                }
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ref T value)
        {
            ulong targetIndex = GetNextIndexForWrite(ReadCurrentIndex());

            _storage[(int)targetIndex].SetObject(ref value);

            Interlocked.Exchange(ref _index, targetIndex);

            ulong count = ReadCurrentCount();

            if (count < (MaxEntries - 1))
            {
                Interlocked.Increment(ref _count);
            }
        }

        public void Clear()
        {
            Interlocked.Exchange(ref _count, 0);
            Interlocked.Exchange(ref _index, 0);
        }

        public static RingLifo<T> Create()
        {
            return new RingLifo<T>
            {
                _bufferCount = MaxEntries,
            };
        }
    }
}

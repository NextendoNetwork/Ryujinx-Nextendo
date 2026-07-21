using Ryujinx.Audio.Backends.Common;
using Ryujinx.Audio.Common;
using Ryujinx.Memory;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.Versioning;
using static Ryujinx.Audio.Backends.Apple.Native.AudioToolbox;

namespace Ryujinx.Audio.Backends.Apple
{
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("ios")]
    class AppleHardwareDeviceSession : HardwareDeviceSessionOutputBase
    {
        private const int NumBuffers = 3;

        private readonly AppleHardwareDeviceDriver _driver;
        private readonly ConcurrentQueue<AppleAudioBuffer> _queuedBuffers = new();
        private readonly DynamicRingBuffer _ringBuffer = new();
        private readonly ManualResetEvent _updateRequiredEvent;

        private readonly AudioQueueOutputCallback _callbackDelegate;
        private readonly GCHandle _gcHandle;

        private nint _audioQueue;
        private readonly nint[] _audioQueueBuffers = new nint[NumBuffers];
        private readonly int[] _bufferBytesFilled = new int[NumBuffers];

        private readonly int _bytesPerFrame;

        private ulong _playedSampleCount;
        private bool _started;
        private float _volume = 1f;

        private readonly object _lock = new();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AudioQueueOutputCallback(
            nint userData,
            nint audioQueue,
            nint buffer);

        public AppleHardwareDeviceSession(
            AppleHardwareDeviceDriver driver,
            IVirtualMemoryManager memoryManager,
            SampleFormat requestedSampleFormat,
            uint requestedSampleRate,
            uint requestedChannelCount)
            : base(memoryManager, requestedSampleFormat, requestedSampleRate, requestedChannelCount)
        {
            _driver = driver;
            _updateRequiredEvent = driver.GetUpdateRequiredEvent();
            _callbackDelegate = OutputCallback;
            _bytesPerFrame = BackendHelper.GetSampleSize(requestedSampleFormat) * (int)requestedChannelCount;

            _gcHandle = GCHandle.Alloc(this, GCHandleType.Normal);

            SetupAudioQueue();
        }

        private void SetupAudioQueue()
        {
            lock (_lock)
            {
                AudioStreamBasicDescription format = AppleHardwareDeviceDriver.GetAudioFormat(
                    RequestedSampleFormat,
                    RequestedSampleRate,
                    RequestedChannelCount);

                nint callbackPtr = Marshal.GetFunctionPointerForDelegate(_callbackDelegate);
                nint userData = GCHandle.ToIntPtr(_gcHandle);

                int result = AudioQueueNewOutput(
                    ref format,
                    callbackPtr,
                    userData,
                    nint.Zero,
                    nint.Zero,
                    0,
                    out _audioQueue);

                if (result != 0)
                {
                    throw new InvalidOperationException($"AudioQueueNewOutput failed: {result}");
                }

                uint framesPerBuffer = RequestedSampleRate / 100;
                uint bufferSize = framesPerBuffer * (uint)_bytesPerFrame;

                for (int i = 0; i < NumBuffers; i++)
                {
                    AudioQueueAllocateBuffer(_audioQueue, bufferSize, out _audioQueueBuffers[i]);
                    _bufferBytesFilled[i] = 0;

                    PrimeBuffer(_audioQueueBuffers[i], i);
                }
            }
        }

        private unsafe void PrimeBuffer(nint bufferPtr, int bufferIndex)
        {
            AudioQueueBuffer* buffer = (AudioQueueBuffer*)bufferPtr;

            int capacityBytes = (int)buffer->AudioDataBytesCapacity;
            int framesPerBuffer = capacityBytes / _bytesPerFrame;

            int availableFrames = _ringBuffer.Length / _bytesPerFrame;
            int framesToRead = Math.Min(availableFrames, framesPerBuffer);
            int bytesToRead = framesToRead * _bytesPerFrame;

            Span<byte> dst = new((void*)buffer->AudioData, capacityBytes);
            dst.Clear();

            if (bytesToRead > 0)
            {
                Span<byte> audio = dst.Slice(0, bytesToRead);
                _ringBuffer.Read(audio, 0, bytesToRead);
                ApplyVolume(buffer->AudioData, bytesToRead);
            }

            buffer->AudioDataByteSize = (uint)capacityBytes;
            _bufferBytesFilled[bufferIndex] = bytesToRead;

            AudioQueueEnqueueBuffer(_audioQueue, bufferPtr, 0, nint.Zero);
        }

        private void OutputCallback(nint userData, nint audioQueue, nint bufferPtr)
        {
            if (!_started || bufferPtr == nint.Zero)
                return;

            int bufferIndex = Array.IndexOf(_audioQueueBuffers, bufferPtr);
            if (bufferIndex < 0)
                return;

            int bytesPlayed = _bufferBytesFilled[bufferIndex];
            if (bytesPlayed > 0)
            {
                ProcessPlayedSamples(bytesPlayed);
            }

            PrimeBuffer(bufferPtr, bufferIndex);
        }

        private void ProcessPlayedSamples(int bytesPlayed)
        {
            ulong samplesPlayed = GetSampleCount(bytesPlayed);
            ulong remaining = samplesPlayed;
            bool needUpdate = false;

            while (remaining > 0 && _queuedBuffers.TryPeek(out AppleAudioBuffer buffer))
            {
                ulong needed = buffer.SampleCount - Interlocked.Read(ref buffer.SamplePlayed);
                ulong take = Math.Min(needed, remaining);

                ulong played = Interlocked.Add(ref buffer.SamplePlayed, take);
                remaining -= take;

                if (played == buffer.SampleCount)
                {
                    _queuedBuffers.TryDequeue(out _);
                    needUpdate = true;
                }

                Interlocked.Add(ref _playedSampleCount, take);
            }

            if (needUpdate)
            {
                _updateRequiredEvent.Set();
            }
        }

        private unsafe void ApplyVolume(nint dataPtr, int byteSize)
        {
            float volume = Math.Clamp(_volume * _driver.Volume, 0f, 1f);
            if (volume >= 0.999f)
                return;

            int sampleCount = byteSize / BackendHelper.GetSampleSize(RequestedSampleFormat);

            switch (RequestedSampleFormat)
            {
                case SampleFormat.PcmInt16:
                    short* s16 = (short*)dataPtr;
                    for (int i = 0; i < sampleCount; i++)
                        s16[i] = (short)(s16[i] * volume);
                    break;

                case SampleFormat.PcmFloat:
                    float* f32 = (float*)dataPtr;
                    for (int i = 0; i < sampleCount; i++)
                        f32[i] *= volume;
                    break;

                case SampleFormat.PcmInt32:
                    int* s32 = (int*)dataPtr;
                    for (int i = 0; i < sampleCount; i++)
                        s32[i] = (int)(s32[i] * volume);
                    break;

                case SampleFormat.PcmInt8:
                    sbyte* s8 = (sbyte*)dataPtr;
                    for (int i = 0; i < sampleCount; i++)
                        s8[i] = (sbyte)(s8[i] * volume);
                    break;
            }
        }

        public override void QueueBuffer(AudioBuffer buffer)
        {
            _ringBuffer.Write(buffer.Data, 0, buffer.Data.Length);
            _queuedBuffers.Enqueue(new AppleAudioBuffer(buffer.DataPointer, GetSampleCount(buffer)));
        }

        public override void Start()
        {
            lock (_lock)
            {
                if (_started)
                    return;

                _started = true;
                AudioQueueStart(_audioQueue, nint.Zero);
            }
        }

        public override void Stop()
        {
            lock (_lock)
            {
                if (!_started)
                    return;

                _started = false;
                AudioQueuePause(_audioQueue);
            }
        }

        public override ulong GetPlayedSampleCount()
            => Interlocked.Read(ref _playedSampleCount);

        public override float GetVolume() => _volume;
        public override void SetVolume(float volume) => _volume = volume;

        public override bool WasBufferFullyConsumed(AudioBuffer buffer)
        {
            if (!_queuedBuffers.TryPeek(out AppleAudioBuffer driverBuffer))
                return true;

            return driverBuffer.DriverIdentifier != buffer.DataPointer;
        }

        public override void PrepareToClose() { }
        public override void UnregisterBuffer(AudioBuffer buffer) { }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();

                if (_audioQueue != nint.Zero)
                {
                    AudioQueueStop(_audioQueue, true);
                    AudioQueueDispose(_audioQueue, true);
                    _audioQueue = nint.Zero;
                }

                if (_gcHandle.IsAllocated)
                {
                    _gcHandle.Free();
                }
            }
        }

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}

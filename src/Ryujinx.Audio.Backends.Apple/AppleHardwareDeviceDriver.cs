using Ryujinx.Audio.Common;
using Ryujinx.Audio.Integration;
using Ryujinx.Common.Logging;
using Ryujinx.Memory;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.Versioning;
using Ryujinx.Audio.Backends.Apple.Native;
using static Ryujinx.Audio.Backends.Apple.Native.AudioToolbox;
using static Ryujinx.Audio.Integration.IHardwareDeviceDriver;

namespace Ryujinx.Audio.Backends.Apple
{
    [SupportedOSPlatform("macos")]
    [SupportedOSPlatform("ios")]
    public sealed class AppleHardwareDeviceDriver : IHardwareDeviceDriver
    {
        private readonly ManualResetEvent _updateRequiredEvent;
        private readonly ManualResetEvent _pauseEvent;
        private readonly ConcurrentDictionary<AppleHardwareDeviceSession, byte> _sessions;
        private readonly bool _supportSurroundConfiguration;

        public float Volume { get; set; }

        public AppleHardwareDeviceDriver()
        {
            _updateRequiredEvent = new ManualResetEvent(false);
            _pauseEvent = new ManualResetEvent(true);
            _sessions = new ConcurrentDictionary<AppleHardwareDeviceSession, byte>();

            _supportSurroundConfiguration = TestSurroundSupport();

            Volume = 1f;
        }

        private bool TestSurroundSupport()
        {
            try
            {
                AudioStreamBasicDescription format =
                    GetAudioFormat(SampleFormat.PcmFloat, Constants.TargetSampleRate, 6);

                int result = AudioQueueNewOutput(
                    ref format,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero,
                    0,
                    out nint testQueue);

                if (result == 0)
                {
                    AudioChannelLayout layout = new AudioChannelLayout
                    {
                        AudioChannelLayoutTag = kAudioChannelLayoutTag_MPEG_5_1_A,
                        AudioChannelBitmap = 0,
                        NumberChannelDescriptions = 0
                    };

                    int layoutResult = AudioQueueSetProperty(
                        testQueue,
                        kAudioQueueProperty_ChannelLayout,
                        ref layout,
                        (uint)Marshal.SizeOf<AudioChannelLayout>());

                    if (layoutResult == 0)
                    {
                        AudioQueueDispose(testQueue, true);
                        return true;
                    }

                    AudioQueueDispose(testQueue, true);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsSupported => OperatingSystem.IsMacOSVersionAtLeast(10, 5);

        public ManualResetEvent GetUpdateRequiredEvent()
            => _updateRequiredEvent;

        public ManualResetEvent GetPauseEvent()
            => _pauseEvent;

        public IHardwareDeviceSession OpenDeviceSession(Direction direction, IVirtualMemoryManager memoryManager,
            SampleFormat sampleFormat, uint sampleRate, uint channelCount)
        {
            if (channelCount == 0)
            {
                channelCount = 2;
            }

            if (sampleRate == 0)
            {
                sampleRate = Constants.TargetSampleRate;
            }

            if (direction != Direction.Output)
            {
                throw new NotImplementedException("Input direction is currently not implemented on Apple backend!");
            }

            AppleHardwareDeviceSession session = new(this, memoryManager, sampleFormat, sampleRate, channelCount);

            _sessions.TryAdd(session, 0);

            return session;
        }

        internal bool Unregister(AppleHardwareDeviceSession session)
            => _sessions.TryRemove(session, out _);

        internal static AudioStreamBasicDescription GetAudioFormat(SampleFormat sampleFormat, uint sampleRate,
            uint channelCount)
        {
            uint formatFlags;
            uint bitsPerChannel;

            switch (sampleFormat)
            {
                case SampleFormat.PcmInt8:
                    formatFlags = kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked;
                    bitsPerChannel = 8;
                    break;
                case SampleFormat.PcmInt16:
                    formatFlags = kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked;
                    bitsPerChannel = 16;
                    break;
                case SampleFormat.PcmInt32:
                    formatFlags = kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked;
                    bitsPerChannel = 32;
                    break;
                case SampleFormat.PcmFloat:
                    formatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
                    bitsPerChannel = 32;
                    break;
                default:
                    throw new ArgumentException($"Unsupported sample format {sampleFormat}");
            }

            uint bytesPerFrame = (bitsPerChannel / 8) * channelCount;

            return new AudioStreamBasicDescription
            {
                SampleRate = sampleRate,
                FormatID = kAudioFormatLinearPCM,
                FormatFlags = formatFlags,
                BytesPerPacket = bytesPerFrame,
                FramesPerPacket = 1,
                BytesPerFrame = bytesPerFrame,
                ChannelsPerFrame = channelCount,
                BitsPerChannel = bitsPerChannel,
                Reserved = 0
            };
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (AppleHardwareDeviceSession session in _sessions.Keys)
                {
                    session.Dispose();
                }

                _pauseEvent.Dispose();
            }
        }

        public bool SupportsDirection(Direction direction)
            => direction != Direction.Input;

        public bool SupportsSampleRate(uint sampleRate) => true;

        public bool SupportsSampleFormat(SampleFormat sampleFormat)
            => sampleFormat != SampleFormat.PcmInt24;

        public bool SupportsChannelCount(uint channelCount)
            => channelCount != 6 || _supportSurroundConfiguration;
    }
}

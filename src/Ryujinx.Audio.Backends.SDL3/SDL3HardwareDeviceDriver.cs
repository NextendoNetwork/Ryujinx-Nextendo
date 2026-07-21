using Ryujinx.Audio.Common;
using Ryujinx.Audio.Integration;
using Ryujinx.Common.Logging;
using Ryujinx.Memory;
using Ryujinx.SDL3.Common;
using System;
using System.Collections.Concurrent;
using System.Threading;
using static Ryujinx.Audio.Integration.IHardwareDeviceDriver;
using SDL;
using static SDL.SDL3;
using System.Runtime.InteropServices;


namespace Ryujinx.Audio.Backends.SDL3
{

    using unsafe SDL_AudioStreamCallbackPointer = delegate* unmanaged[Cdecl]<nint, SDL_AudioStream*, int, int, void>;

    public sealed class SDL3HardwareDeviceDriver : IHardwareDeviceDriver
    {
        private readonly ManualResetEvent _updateRequiredEvent;
        private readonly ManualResetEvent _pauseEvent;
        private readonly ConcurrentDictionary<SDL3HardwareDeviceSession, byte> _sessions;

        private readonly bool _supportSurroundConfiguration;

        public float Volume { get; set; }

        public unsafe SDL3HardwareDeviceDriver()
        {
            _updateRequiredEvent = new ManualResetEvent(false);
            _pauseEvent = new ManualResetEvent(true);
            _sessions = new ConcurrentDictionary<SDL3HardwareDeviceSession, byte>();

            SDL3Driver.Instance.Initialize();

            SDL_AudioSpec spec;
            if (!SDL_GetAudioDeviceFormat(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, null))
            {
                Logger.Error?.Print(LogClass.Application,
                    $"SDL_GetDefaultAudioInfo failed with error \"{SDL_GetError()}\"");

                _supportSurroundConfiguration = true;
            }
            else
            {
                _supportSurroundConfiguration = spec.channels >= 6;
            }

            Volume = 1f;
        }

        public static bool IsSupported => IsSupportedInternal();

        private unsafe static bool IsSupportedInternal()
        {
            SDL_AudioStream* device = OpenStream(SampleFormat.PcmInt16, Constants.TargetSampleRate, Constants.ChannelCountMax, Constants.TargetSampleCount, null);

            if (device != null)
            {
                SDL_DestroyAudioStream(device);
            }

            return device != null;
        }

        public ManualResetEvent GetUpdateRequiredEvent()
        {
            return _updateRequiredEvent;
        }

        public ManualResetEvent GetPauseEvent()
        {
            return _pauseEvent;
        }

        public IHardwareDeviceSession OpenDeviceSession(Direction direction, IVirtualMemoryManager memoryManager, SampleFormat sampleFormat, uint sampleRate, uint channelCount)
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
                throw new NotImplementedException("Input direction is currently not implemented on SDL3 backend!");
            }

            SDL3HardwareDeviceSession session = new(this, memoryManager, sampleFormat, sampleRate, channelCount);

            _sessions.TryAdd(session, 0);

            return session;
        }

        internal bool Unregister(SDL3HardwareDeviceSession session)
        {
            return _sessions.TryRemove(session, out _);
        }

        private static SDL_AudioSpec GetSDL3Spec(SampleFormat requestedSampleFormat, uint requestedSampleRate, uint requestedChannelCount)
        {
            return new SDL_AudioSpec
            {
                channels = (byte)requestedChannelCount,
                format = GetSDL3Format(requestedSampleFormat),
                freq = (int)requestedSampleRate,
            };
        }

        internal static SDL_AudioFormat GetSDL3Format(SampleFormat format)
        {
            return format switch
            {
                SampleFormat.PcmInt8 => SDL_AudioFormat.SDL_AUDIO_S8,
                SampleFormat.PcmInt16 => SDL_AudioFormat.SDL_AUDIO_S16LE,
                SampleFormat.PcmInt32 => SDL_AudioFormat.SDL_AUDIO_S32LE,
                SampleFormat.PcmFloat => SDL_AudioFormat.SDL_AUDIO_F32LE,
                _ => throw new ArgumentException($"Unsupported sample format {format}"),
            };
        }

        internal unsafe static SDL_AudioStream* OpenStream(SampleFormat requestedSampleFormat, uint requestedSampleRate, uint requestedChannelCount, uint sampleCount, SDL3HardwareDeviceSession.SDL_AudioStreamCallback callback)
        {
            SDL_AudioSpec desired = GetSDL3Spec(requestedSampleFormat, requestedSampleRate, requestedChannelCount);
            SDL_AudioSpec got = desired;
            var pCallback = callback != null ? (SDL_AudioStreamCallbackPointer)Marshal.GetFunctionPointerForDelegate(callback) : null;

            // From SDL 3 and on, SDL requires us to set this as a hint
            SDL_SetHint(SDL_HINT_AUDIO_DEVICE_SAMPLE_FRAMES, $"{sampleCount}");
            SDL_AudioStream* device = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &got, pCallback, 0);

            if (device == null)
            {
                Logger.Error?.Print(LogClass.Application, $"SDL3 open audio device initialization failed with error \"{SDL_GetError()}\"");

                return null;
            }

            bool isValid = got.format == desired.format && got.freq == desired.freq && got.channels == desired.channels;

            if (!isValid)
            {
                Logger.Error?.Print(LogClass.Application, "SDL3 open audio device is not valid");
                SDL_DestroyAudioStream(device);

                return null;
            }

            return device;
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
                foreach (SDL3HardwareDeviceSession session in _sessions.Keys)
                {
                    session.Dispose();
                }

                SDL3Driver.Instance.Dispose();

                _pauseEvent.Dispose();
            }
        }

        public bool SupportsSampleRate(uint sampleRate)
        {
            return true;
        }

        public bool SupportsSampleFormat(SampleFormat sampleFormat)
        {
            return sampleFormat != SampleFormat.PcmInt24;
        }

        public bool SupportsChannelCount(uint channelCount)
        {
            if (channelCount == 6)
            {
                return _supportSurroundConfiguration;
            }

            return true;
        }

        public bool SupportsDirection(Direction direction)
        {
            // TODO: add direction input when supported.
            return direction == Direction.Output;
        }
    }
}

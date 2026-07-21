using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming

namespace Ryujinx.Audio.Backends.Apple.Native
{
    public static partial class AudioToolbox
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct AudioStreamBasicDescription
        {
            public double SampleRate;
            public uint FormatID;
            public uint FormatFlags;
            public uint BytesPerPacket;
            public uint FramesPerPacket;
            public uint BytesPerFrame;
            public uint ChannelsPerFrame;
            public uint BitsPerChannel;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AudioChannelLayout
        {
            public uint AudioChannelLayoutTag;
            public uint AudioChannelBitmap;
            public uint NumberChannelDescriptions;
        }

        internal const uint kAudioFormatLinearPCM = 0x6C70636D;
        internal const uint kAudioQueueProperty_ChannelLayout = 0x6171636c;
        internal const uint kAudioChannelLayoutTag_MPEG_5_1_A = 0x650006;
        internal const uint kAudioFormatFlagIsFloat = (1 << 0);
        internal const uint kAudioFormatFlagIsSignedInteger = (1 << 2);
        internal const uint kAudioFormatFlagIsPacked = (1 << 3);
        internal const uint kAudioFormatFlagIsBigEndian = (1 << 1);
        internal const uint kAudioFormatFlagIsAlignedHigh = (1 << 4);
        internal const uint kAudioFormatFlagIsNonInterleaved = (1 << 5);

        [LibraryImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static partial int AudioQueueNewOutput(
            ref AudioStreamBasicDescription format,
            nint callback,
            nint userData,
            nint callbackRunLoop,
            nint callbackRunLoopMode,
            uint flags,
            out nint audioQueue);

        [LibraryImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static partial int AudioQueueSetProperty(
            nint audioQueue,
            uint propertyID,
            ref AudioChannelLayout layout,
            uint layoutSize);

        [LibraryImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static partial int AudioQueueDispose(nint audioQueue, [MarshalAs(UnmanagedType.I1)] bool immediate);

        [LibraryImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static partial int AudioQueueAllocateBuffer(
            nint audioQueue,
            uint bufferByteSize,
            out nint buffer);

        [LibraryImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static partial int AudioQueueStart(nint audioQueue, nint startTime);

        [LibraryImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static partial int AudioQueuePause(nint audioQueue);

        [LibraryImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static partial int AudioQueueStop(nint audioQueue, [MarshalAs(UnmanagedType.I1)] bool immediate);

        [LibraryImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static partial int AudioQueueSetParameter(
            nint audioQueue,
            uint parameterID,
            float value);

        [LibraryImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
        internal static partial int AudioQueueEnqueueBuffer(
            nint audioQueue,
            nint buffer,
            uint numPacketDescs,
            nint packetDescs);

        [StructLayout(LayoutKind.Sequential)]
        internal struct AudioQueueBuffer
        {
            public uint AudioDataBytesCapacity;
            public nint AudioData;
            public uint AudioDataByteSize;
            public nint UserData;
            public uint PacketDescriptionCapacity;
            public nint PacketDescriptions;
            public uint PacketDescriptionCount;
        }

        internal const uint kAudioQueueParam_Volume = 1;
    }
}

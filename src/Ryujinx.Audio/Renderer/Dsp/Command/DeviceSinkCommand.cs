using Ryujinx.Audio.Integration;
using Ryujinx.Audio.Renderer.Server.Sink;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class DeviceSinkCommand : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.DeviceSink;

        public uint EstimatedProcessingTime { get; set; }

        public string DeviceName { get; private set; }

        public int SessionId { get; private set; }

        public uint InputCount { get; private set; }
        public ushort[] InputBufferIndices { get; private set; }

        public Memory<float> Buffers { get; private set; }

        public DeviceSinkCommand()
        {
            
        }

        public DeviceSinkCommand Initialize(uint bufferOffset, DeviceSink sink, int sessionId, Memory<float> buffers, int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;

            // Unused and wasting time and memory, re-add if needed
            // DeviceName = Encoding.ASCII.GetString(sink.Parameter.DeviceName).TrimEnd('\0');
            
            SessionId = sessionId;
            InputCount = sink.Parameter.InputCount;
            InputBufferIndices = new ushort[InputCount];
            
            Span<byte> inputSpan = sink.Parameter.Input.AsSpan();

            for (int i = 0; i < Math.Min(InputCount, Constants.ChannelCountMax); i++)
            {
                InputBufferIndices[i] = (ushort)(bufferOffset + inputSpan[i]);
            }

            if (sink.UpsamplerInfo != null)
            {
                Buffers = sink.UpsamplerInfo.OutputBuffer;
            }
            else
            {
                Buffers = buffers;
            }

            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Span<float> GetBuffer(int index, int sampleCount)
        {
            return Buffers.Span.Slice(index * sampleCount, sampleCount);
        }

        public void Process(CommandList context)
        {
            IHardwareDevice device = context.OutputDevice;

            if (device.GetSampleRate() == Constants.TargetSampleRate)
            {
                int channelCount = (int)device.GetChannelCount();
                uint bufferCount = Math.Min(device.GetChannelCount(), InputCount);

                const int SampleCount = Constants.TargetSampleCount;

                uint inputCount;

                // In case of upmixing to 5.1, we allocate the right amount.
                if (bufferCount != channelCount && channelCount == 6)
                {
                    inputCount = (uint)channelCount;
                }
                else
                {
                    inputCount = bufferCount;
                }

                short[] outputBuffer = ArrayPool<short>.Shared.Rent((int)inputCount * SampleCount);
                Array.Fill(outputBuffer, (short)0, 0, (int)inputCount * SampleCount);

                for (int i = 0; i < bufferCount; i++)
                {
                    ReadOnlySpan<float> inputBuffer = GetBuffer(InputBufferIndices[i], SampleCount);

                    for (int j = 0; j < SampleCount; j++)
                    {
                        outputBuffer[i + j * channelCount] = PcmHelper.Saturate(inputBuffer[j]);
                    }
                }

                device.AppendBuffer(outputBuffer.AsSpan(..((int)inputCount * SampleCount)), inputCount);
                
                ArrayPool<short>.Shared.Return(outputBuffer);
            }
            else
            {
                // TODO: support resampling for device only supporting something different
                throw new NotImplementedException();
            }
        }
    }
}

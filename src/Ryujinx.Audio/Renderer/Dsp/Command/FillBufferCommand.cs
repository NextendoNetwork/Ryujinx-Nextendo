using Ryujinx.Audio.Renderer.Server.Splitter;
using System.Runtime.CompilerServices;

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class FillBufferCommand : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.FillBuffer;

        public uint EstimatedProcessingTime { get; set; }

        public SplitterDestinationVersion1 Destination1 { get; private set; }
        public SplitterDestinationVersion2 Destination2 { get; private set; }
        public bool IsV2 { get; private set; }
        public int Length { get; private set; }
        public float Value { get; private set; }

        public FillBufferCommand()
        {
            
        }
        
        public FillBufferCommand Initialize(SplitterDestination destination, int length, float value, int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;

            if (Unsafe.IsNullRef(ref destination.GetV2RefOrNull()))
            {
                Destination1 = destination.GetV1RefOrNull();
                IsV2 = false;
            }
            else
            {
                Destination2 = destination.GetV2RefOrNull();
                IsV2 = true;
            }
            
            Length = length;
            Value = value;

            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessFillBuffer()
        {
            if (IsV2)
            {
                for (int i = 0; i < Length; i++)
                {
                    Destination2.PreviousMixBufferVolume[i] = Value;
                }
            }
            else
            {
                for (int i = 0; i < Length; i++)
                {
                    Destination1.PreviousMixBufferVolume[i] = Value;
                }
            }
        }

        public void Process(CommandList context)
        {
            ProcessFillBuffer();
        }
    }
}

namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class CopyMixBufferCommand : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.CopyMixBuffer;

        public uint EstimatedProcessingTime { get; set; }

        public ushort InputBufferIndex { get; private set; }
        public ushort OutputBufferIndex { get; private set; }

        public CopyMixBufferCommand()
        {
            
        }

        public CopyMixBufferCommand Initialize(uint inputBufferIndex, uint outputBufferIndex, int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;

            InputBufferIndex = (ushort)inputBufferIndex;
            OutputBufferIndex = (ushort)outputBufferIndex;

            return this;
        }

        public void Process(CommandList context)
        {
            context.CopyBuffer(OutputBufferIndex, InputBufferIndex);
        }
    }
}

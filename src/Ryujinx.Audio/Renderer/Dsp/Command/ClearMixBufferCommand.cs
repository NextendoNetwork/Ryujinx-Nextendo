namespace Ryujinx.Audio.Renderer.Dsp.Command
{
    public class ClearMixBufferCommand : ICommand
    {
        public bool Enabled { get; set; }

        public int NodeId { get; private set; }

        public CommandType CommandType => CommandType.ClearMixBuffer;

        public uint EstimatedProcessingTime { get; set; }

        public ClearMixBufferCommand()
        {
            
        }

        public ClearMixBufferCommand Initialize(int nodeId)
        {
            Enabled = true;
            NodeId = nodeId;

            return this;
        }

        public void Process(CommandList context)
        {
            context.ClearBuffers();
        }
    }
}

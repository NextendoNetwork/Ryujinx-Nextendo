using Ryujinx.Cpu;

namespace Ryujinx.HLE.Debugger
{
    public enum MessageType
    {
        Kill,
        BreakIn,
        SendNack
    }

    record struct Message(MessageType Type) : Message.IMarker
    {
        /// <summary>
        ///     Marker interface for debugger messages.
        /// </summary>
        internal interface IMarker;

        public static Message Kill => new(MessageType.Kill);
        public static Message BreakIn => new(MessageType.BreakIn);
        public static Message SendNack => new(MessageType.SendNack);
    }

    struct CommandMessage : Message.IMarker
    {
        public readonly string Command;

        public CommandMessage(string cmd)
            => Command = cmd;
    }

    public class ThreadBreakMessage : Message.IMarker
    {
        public IExecutionContext Context { get; }
        public ulong Address { get; }
        public int Opcode { get; }

        public ThreadBreakMessage(IExecutionContext context, ulong address, int opcode)
        {
            Context = context;
            Address = address;
            Opcode = opcode;
        }
    }
}

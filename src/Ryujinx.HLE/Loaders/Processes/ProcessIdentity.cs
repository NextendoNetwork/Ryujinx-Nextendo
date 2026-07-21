namespace Ryujinx.HLE.Loaders.Processes
{
    public readonly struct ProcessIdentity
    {
        public ulong ProcessId { get; }
        public ulong ProgramId { get; }
        public ulong ApplicationId { get; }
        public byte ProgramIndex { get; }
        public string ProgramIdText { get; }
        public string DisplayVersion { get; }
        public ProcessKind Kind { get; }

        public ProcessIdentity(
            ulong processId,
            ulong programId,
            byte programIndex,
            string displayVersion,
            ProcessKind kind)
        {
            ProcessId = processId;
            ProgramId = programId;
            ProgramIndex = programIndex;
            ApplicationId = programId & ~0xFul;
            ProgramIdText = $"{programId:x16}";
            DisplayVersion = displayVersion ?? string.Empty;
            Kind = kind;
        }

        public override string ToString()
        {
            return $"{Kind} pid={ProcessId} program={ProgramIdText} application={ApplicationId:x16} index={ProgramIndex} version={DisplayVersion}";
        }
    }
}

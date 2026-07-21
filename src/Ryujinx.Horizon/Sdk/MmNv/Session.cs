namespace Ryujinx.Horizon.Sdk.MmNv
{
    class Session
    {
        public Module Module { get; }
        public uint Id { get; }
        public bool IsAutoClearEvent { get; }
        public uint RequestedClockRate { get; private set; }
        public uint LastTimeout { get; private set; }
        public bool HasActiveRequest { get; private set; }

        public Session(uint id, Module module, bool isAutoClearEvent)
        {
            Module = module;
            Id = id;
            IsAutoClearEvent = isAutoClearEvent;
            RequestedClockRate = 0;
            LastTimeout = uint.MaxValue;
            HasActiveRequest = false;
        }

        public void SetAndWait(uint clockRate, uint timeout)
        {
            RequestedClockRate = clockRate;
            LastTimeout = timeout;
            HasActiveRequest = true;
        }

        public void ClearRequest(uint timeout)
        {
            RequestedClockRate = 0;
            LastTimeout = timeout;
            HasActiveRequest = false;
        }
    }
}

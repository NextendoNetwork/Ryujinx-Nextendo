namespace Ryujinx.HLE.Debugger
{
    enum GdbSignal
    {
        Zero = 0,
        Int = 2,
        Quit = 3,
        Trap = 5,
        Abort = 6,
        Alarm = 14,
        IO = 23,
        XCPU = 24,
        Unknown = 143
    }
}

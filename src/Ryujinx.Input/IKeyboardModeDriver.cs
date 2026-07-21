namespace Ryujinx.Input
{
    public interface IKeyboardModeDriver : IGamepadDriver
    {
        IKeyboard GetKeyboard(string id, KeyboardInputMode mode);
    }
}

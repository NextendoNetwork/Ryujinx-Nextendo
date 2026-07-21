using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Drawing;
using System.Numerics;

namespace Ryujinx.Input.SDL3
{
    public class SDL3Mouse : IMouse
    {
        private SDL3MouseDriver _driver;

        public GamepadFeaturesFlag Features => throw new NotImplementedException();

        public string Id => "0";

        public string Name => "SDL3Mouse";

        public bool IsConnected => true;

        public bool[] Buttons => _driver.PressedButtons;

        Size IMouse.ClientSize => _driver.GetClientSize();

        public SDL3Mouse(SDL3MouseDriver driver)
        {
            _driver = driver;
        }

        public Vector2 GetPosition()
        {
            return _driver.CurrentPosition;
        }

        public Vector2 GetScroll()
        {
            return _driver.Scroll;
        }

        public GamepadStateSnapshot GetMappedStateSnapshot()
        {
            throw new NotImplementedException();
        }

        public Vector3 GetMotionData(MotionInputId inputId)
        {
            throw new NotImplementedException();
        }

        public GamepadStateSnapshot GetStateSnapshot()
        {
            throw new NotImplementedException();
        }

        public (float, float) GetStick(StickInputId inputId)
        {
            throw new NotImplementedException();
        }

        public bool IsButtonPressed(MouseButton button)
        {
            return _driver.IsButtonPressed(button);
        }

        public bool IsPressed(GamepadButtonInputId inputId)
        {
            throw new NotImplementedException();
        }

        public bool HDRumble(VibrationValue left, VibrationValue right)
        {
            throw new NotImplementedException();
        }

        public bool Rumble(float lowFrequency, float highFrequency, uint durationMs)
        {
            throw new NotImplementedException();
        }

        public void SetConfiguration(InputConfig configuration)
        {
            throw new NotImplementedException();
        }

        public void SetLed(uint packedRgb)
        {
            Logger.Info?.Print(LogClass.UI, "SetLed called on an SDL3Mouse");
        }

        public void SetTriggerThreshold(float triggerThreshold)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _driver = null;
        }
    }
}

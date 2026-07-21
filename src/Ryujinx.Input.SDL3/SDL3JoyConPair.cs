using Gommon;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Hid;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SDL;
using static SDL.SDL3;

namespace Ryujinx.Input.SDL3
{
    internal class SDL3JoyConPair(IGamepad left, IGamepad right) : IGamepad
    {
        public GamepadFeaturesFlag Features => (left?.Features ?? GamepadFeaturesFlag.None) |
                                               (right?.Features ?? GamepadFeaturesFlag.None);

        public const string Id = "JoyConPair";
        string IGamepad.Id => Id;

        public string Name => "Nintendo Switch Dual Joy-Con (L/R)";
        public bool IsConnected => left is { IsConnected: true } && right is { IsConnected: true };

        public void Dispose()
        {
            left?.Dispose();
            right?.Dispose();
        }

        public GamepadStateSnapshot GetMappedStateSnapshot()
        {
            return GetStateSnapshot();
        }

        public Vector3 GetMotionData(MotionInputId inputId)
        {
            return inputId switch
            {
                MotionInputId.Accelerometer or
                    MotionInputId.Gyroscope => left.GetMotionData(inputId),
                MotionInputId.SecondAccelerometer => right.GetMotionData(MotionInputId.Accelerometer),
                MotionInputId.SecondGyroscope => right.GetMotionData(MotionInputId.Gyroscope),
                _ => Vector3.Zero
            };
        }

        public GamepadStateSnapshot GetStateSnapshot()
        {
            return IGamepad.GetStateSnapshot(this);
        }

        public (float, float) GetStick(StickInputId inputId)
        {
            return inputId switch
            {
                StickInputId.Left => left.GetStick(StickInputId.Left),
                StickInputId.Right => right.GetStick(StickInputId.Right),
                _ => (0, 0)
            };
        }

        public bool IsPressed(GamepadButtonInputId inputId)
        {
            return left.IsPressed(inputId) || right.IsPressed(inputId);
        }

        public bool HDRumble(VibrationValue left, VibrationValue right)
        {
            // return _hdRumble?.HdRumble(left, right) ?? false;
            // TODO: Track rumble and motion on both controllers
            return false;
        }

        public bool Rumble(float lowFrequency, float highFrequency, uint durationMs)
        {
            if (lowFrequency != 0)
            {
                right.Rumble(lowFrequency, lowFrequency, durationMs);
            }

            if (highFrequency != 0)
            {
                left.Rumble(highFrequency, highFrequency, durationMs);
            }

            if (lowFrequency == 0 && highFrequency == 0)
            {
                left.Rumble(0, 0, durationMs);
                right.Rumble(0, 0, durationMs);
            }

            if (!SDL_GetError().IsNullOrEmpty())
            {
                Logger.Error?.PrintMsg(LogClass.Hid, SDL_GetError());
                SDL_ClearError();
                return false;
            }

            return true;
        }

        public void SetConfiguration(InputConfig configuration)
        {
            left.SetConfiguration(configuration);
            right.SetConfiguration(configuration);
        }

        public void SetLed(uint packedRgb)
        {
        }

        public void SetTriggerThreshold(float triggerThreshold)
        {
            left.SetTriggerThreshold(triggerThreshold);
            right.SetTriggerThreshold(triggerThreshold);
        }

        public static bool IsCombinable(SDL_JoystickID joyCon1, Dictionary<SDL_JoystickID, string> joyConIds, out SDL_JoystickID match)
        {
            bool isLeft = SDL3JoyCon.IsLeftJoyCon(joyCon1);
            string matchName = isLeft ? SDL3JoyCon.RightName : SDL3JoyCon.LeftName;
            match = 0;

            foreach (var joyConId in joyConIds.Keys)
            {
                if (SDL_GetGamepadNameForID(joyConId) == matchName)
                {
                    match = joyConId;
                    
                    return true;
                }
            }
            
            return false;
        }
    }
}

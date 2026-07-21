using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Hid;
using SDL;
using static SDL.SDL3;
using System;

namespace Ryujinx.Input.SDL3
{
    /// <summary>
    /// Manages a HID handle of a gamepad to encode and write HD rumble commands for Nin controllers.
    /// </summary>
    public unsafe class NpadHdRumble : IDisposable
    {
        private readonly SDL_hid_device* _hidHandle;
        
        private byte[] _buffer;
        private static ushort _vendor;
        private static ushort _product;
        
        private int _globalCount;
        private ulong _lastWriteTicks;

        private NpadHdRumble(SDL_hid_device* hidHandle)
        {
            _hidHandle = hidHandle;
            InitializeDevice();
        }

        public static NpadHdRumble Create(SDL_Gamepad* gamepadHandle)
        {
            _vendor = SDL_GetGamepadVendor(gamepadHandle);
            if (!Enum.IsDefined(typeof(HDRumbleSupportedVendor), _vendor))
            {
                return null;
            }

            _product = SDL_GetGamepadProduct(gamepadHandle);
            if (!Enum.IsDefined(typeof(HDRumbleSupportedProduct), _product))
            {
                return null;
            }

            int serialNumber = 0;
            string serial = SDL_GetGamepadSerial(gamepadHandle);
            if (serial is not null)
            {
                int.TryParse(serial, out serialNumber);
            }
            
            return new NpadHdRumble(SDL_hid_open(_vendor, _product, serialNumber));
        }

        // Some of the code was translated from https://github.com/MIZUSHIKI/JoyShockLibrary-plus-HDRumble
        private bool WriteNintendoHdRumble(VibrationValue left, VibrationValue right)
        {
            int leftLowAmp = EncodeLowAmp(left.AmplitudeLow);
            int leftLowFreq = EncodeLowFreq(left.FrequencyLow) + (leftLowAmp >> 8);
            int leftHighFreq = EncodeHighFreq(left.FrequencyHigh);
            int leftHighAmp = EncodeHighAmp(left.AmplitudeHigh) + (leftHighFreq >> 8);
                
            int rightLowAmp = EncodeLowAmp(right.AmplitudeLow);
            int rightLowFreq = EncodeLowFreq(right.FrequencyLow) + (rightLowAmp >> 8);
            int rightHighFreq = EncodeHighFreq(right.FrequencyHigh);
            int rightHighAmp = EncodeHighAmp(right.AmplitudeHigh) + (rightHighFreq >> 8);
            
            _buffer[0] = 0x10;
            _buffer[1] = (byte)((_globalCount++) & 0xF);
            
            // Left LRA
            _buffer[2] = (byte)(leftLowFreq & 0xFF);
            _buffer[3] = (byte)(leftHighAmp & 0xFF);
            _buffer[4] = (byte)(leftHighFreq & 0xFF);
            _buffer[5] = (byte)(leftLowAmp & 0xFF);
            
            // Right LRA
            _buffer[6] = (byte)(rightLowFreq & 0xFF);
            _buffer[7] = (byte)(rightHighAmp & 0xFF);
            _buffer[8] = (byte)(rightHighFreq & 0xFF);
            _buffer[9] = (byte)(rightLowAmp & 0xFF);
            
            if (_globalCount > 0xF)
            {
                _globalCount = 0x0;
            }
            
            fixed (byte* ptr = _buffer)
            {
                if (SendHdRumble(ptr, (nuint)_buffer.Length) >= 0)
                {
                    return true;
                }
                
                Logger.Error?.PrintMsg(LogClass.Hid, SDL_GetError());
                SDL_ClearError();
            }
            
            return false;
        }

        private static int EncodeLowFreq(float lowFreq)
        {
            return (int)Math.Clamp(32 * Math.Log2(lowFreq * 0.1f) - 0x40, 81.75177f, 1252.572266f);
        }

        private static int EncodeHighFreq(float highFreq)
        {
            return (int)Math.Clamp(32 * Math.Log2(highFreq * 0.1f) - 0x60, 81.75177f, 1252.572266f);
        }

        private static int EncodeLowAmp(float rawAmp)
        {
            double encodedAmp = 0;

            if (rawAmp is > 0 and < 0.012f)
                encodedAmp = 1;
            
            else if (rawAmp is >= 0.012f and < 0.112f)
                encodedAmp = 4 * Math.Log2(rawAmp * 110f);
            
            else if (rawAmp is >= 0.112f and < 0.225f)
                encodedAmp = 16 * Math.Log2(rawAmp * 17f);
            
            else if (rawAmp is >= 0.225f and <= 1f)
                encodedAmp = 32 * Math.Log2(rawAmp * 8.7f);
            
            encodedAmp = Math.Round((encodedAmp / 2.0) + 64.0);
            encodedAmp = Math.Clamp(encodedAmp, 0.0, 100.2867);
            return (int)Math.Round(encodedAmp);
        }

        private static int EncodeHighAmp(float rawAmp)
        {
            double encodedAmp = 0;

            if (rawAmp is > 0 and < 0.012f)
                encodedAmp = 1;
            
            else if (rawAmp is >= 0.012f and < 0.112f)
                encodedAmp = 4 * Math.Log2(rawAmp * 110f);
            
            else if (rawAmp is >= 0.112f and < 0.225f)
                encodedAmp = 16 * Math.Log2(rawAmp * 17f);
            
            else if (rawAmp is >= 0.225f and <= 1f)
                encodedAmp = 32 * Math.Log2(rawAmp * 8.7f);
            
            encodedAmp = Math.Round(encodedAmp / 2.0);
            encodedAmp = Math.Clamp(encodedAmp, 0.0, 100.2867);
            return (int)encodedAmp;
        }

        public bool HdRumble(VibrationValue left, VibrationValue right)
        {
            if(_product is (ushort) HDRumbleSupportedProduct.ProController
               or (ushort) HDRumbleSupportedProduct.JoyconLeft
               or (ushort) HDRumbleSupportedProduct.JoyconRight
               or (ushort) HDRumbleSupportedProduct.JoyconPair
               or (ushort) HDRumbleSupportedProduct.JoyconGrip)
            {
                return WriteNintendoHdRumble(left, right);
            }

            return false;
        }

        private int SendHdRumble(byte* data, nuint length)
        {
            int result = 0;
            ulong currentTicks = SDL_GetTicks();

            // Ditch rumble if we haven't hit the poll-rate yet.
            if ((currentTicks - _lastWriteTicks) <= GetPollRate())
            {
                return result;
            }
            
            result = SDL_hid_write(_hidHandle, data, length);
            if (result >= 0)
            {
                _lastWriteTicks = currentTicks;
            }
            
            return result;
        }

        private void InitializeDevice()
        {
            if (_vendor is (ushort)HDRumbleSupportedVendor.Nintendo)
            {
                _buffer = new byte[10];
                byte[] init = new byte[64];

                // Pro Controller and Charge Grip
                if (_product 
                    is (ushort)HDRumbleSupportedProduct.ProController 
                    or (ushort)HDRumbleSupportedProduct.JoyconGrip)
                {
                    SDL_LockJoysticks();
                    fixed (byte* ptr = init)
                    {
                        init[0] = 0x80;
                        init[1] = 0x05; // Allow bluetooth timeout TODO: use 0x04 to force USB only (toggle?)
                        SDL_hid_write(_hidHandle, ptr, 64);
                    }
                    SDL_UnlockJoysticks();
                
                    return;
                }
            
                // Joycons
                if (_product 
                    is (ushort)HDRumbleSupportedProduct.JoyconLeft 
                    or (ushort)HDRumbleSupportedProduct.JoyconRight
                    or (ushort)HDRumbleSupportedProduct.JoyconPair)
                {
                
                    SDL_LockJoysticks();
                    fixed (byte* ptr = init)
                    {
                        // we could write data to the controller here (see above)
                    }
                    SDL_UnlockJoysticks();
                    
                    return;
                }
            }
        }
        
        private ulong GetPollRate()
        {
            ulong pollRate = 0;
            if (_vendor is (ushort)HDRumbleSupportedVendor.Nintendo)
            {
                // https://docs.handheldlegend.com/s/progcc-3/doc/lag-comparison-aAR1mV3JLX
                pollRate = (ulong) 16.67;
                if (_product is (ushort)HDRumbleSupportedProduct.ProController
                    && SDL_hid_get_device_info(_hidHandle)->bus_type == SDL_hid_bus_type.SDL_HID_API_BUS_USB)
                {
                    pollRate = (ulong) 8.33;
                }
            }
            
            return pollRate;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            SDL_hid_close(_hidHandle);
        }
    }

    public enum HDRumbleSupportedVendor : ushort
    {
        Nintendo = 0x057e,
        Valve = 0x28de,
        Sony = 0x054c
    }

    public enum HDRumbleSupportedProduct : ushort
    {
        // TODO: Currently, HD Rumble only supports the Pro Controller and JoyCons.
        //       We need to initialize and report to each device differently.
        
        // Nintendo Switch: 0x057e
        JoyconLeft = 0x2006,
        JoyconRight = 0x2007,
        JoyconPair = 0x2008,
        ProController = 0x2009,
        JoyconGrip = 0x200e,
        
        // Nintendo Switch 2: 0x057e
        Joycon2Right = 0x2066,
        Joycon2Left = 0x2067,
        Joycon2Pair = 0x2068,
        Switch2ProController = 0x2069,
        GamecubeController = 0x2073,
        
        // Valve Steam Family: 0x28de
        // https://github.com/libsdl-org/SDL/issues/9148
        SteamDeck = 0x11ff,
        SteamDeckVirtualDevice = 0x1205,
        SteamController = 0x1106,
        
        // PlayStation Dualsense: 0x054c
        Dualsense = 0x0ce6
    }
}

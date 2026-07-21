using Ryujinx.SDL3.Common;
using System;
using System.Collections.Generic;

namespace Ryujinx.Input.SDL3
{
    public class SDL3KeyboardDriver : IGamepadDriver
    {
        public SDL3KeyboardDriver()
        {
            SDL3Driver.Instance.Initialize();
        }

        public string DriverName => "SDL3";

        private static readonly string[] _keyboardIdentifers = ["0"];

        public ReadOnlySpan<string> GamepadsIds => _keyboardIdentifers;

        public event Action<string> OnGamepadConnected
        {
            add { }
            remove { }
        }

        public event Action<string> OnGamepadDisconnected
        {
            add { }
            remove { }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                SDL3Driver.Instance.Dispose();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        public IGamepad GetGamepad(string id)
        {
            if (!_keyboardIdentifers[0].Equals(id))
            {
                return null;
            }

            return new SDL3Keyboard(_keyboardIdentifers[0], "All keyboards");
        }

        public IEnumerable<IGamepad> GetGamepads()
        {
            foreach (string keyboardId in _keyboardIdentifers)
            {
                yield return GetGamepad(keyboardId);
            }
        }
    }
}

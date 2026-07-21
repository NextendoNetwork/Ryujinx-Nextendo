using Microsoft.IO;
using Ryujinx.Common.Memory;
using Ryujinx.HLE.HOS.Services.Account.Acc;
using Ryujinx.HLE.HOS.Services.Am.AppletAE;
using System;
using System.IO;

namespace Ryujinx.HLE.HOS.Applets
{
    internal class PlayerSelectApplet : IApplet
    {
        private readonly Horizon _system;

        private AppletSession _normalSession;
#pragma warning disable IDE0052 // Remove unread private member
        private AppletSession _interactiveSession;
#pragma warning restore IDE0052

        public event EventHandler AppletStateChanged;

        public PlayerSelectApplet(Horizon system)
        {
            _system = system;
        }

        public ResultCode Start(AppletSession normalSession, AppletSession interactiveSession)
        {
            _normalSession = normalSession;
            _interactiveSession = interactiveSession;

            UserProfile selected = _system.Device.UIHandler.ShowPlayerSelectDialog();
            if (selected == null)
            {
                _normalSession.Push(BuildResponse());
            }
            else if (selected.UserId == new UserId("00000000000000000000000000000080"))
            {
                _normalSession.Push(BuildGuestResponse());
            }
            else
            {
                _normalSession.Push(BuildResponse(selected));
            }

            AppletStateChanged?.Invoke(this, null);

            _system.ReturnFocus();

            return ResultCode.Success;
        }

        private static byte[] BuildResponse(UserProfile selectedUser)
        {
            using RecyclableMemoryStream stream = MemoryStreamManager.Shared.GetStream();
            using BinaryWriter writer = new(stream);

            writer.Write((ulong)PlayerSelectResult.Success);

            selectedUser.UserId.Write(writer);

            return stream.ToArray();
        }

        private static byte[] BuildGuestResponse()
        {
            using RecyclableMemoryStream stream = MemoryStreamManager.Shared.GetStream();
            using BinaryWriter writer = new(stream);

            writer.Write(new byte());

            return stream.ToArray();
        }

        private static byte[] BuildResponse()
        {
            using RecyclableMemoryStream stream = MemoryStreamManager.Shared.GetStream();
            using BinaryWriter writer = new(stream);

            writer.Write((ulong)PlayerSelectResult.Failure);

            return stream.ToArray();
        }
    }
}

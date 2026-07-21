using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Caps.Types;

namespace Ryujinx.HLE.HOS.Services.Caps
{
    [Service("caps:su")] // 6.0.0+
    internal class IScreenShotApplicationService : IpcService
    {
        private const ulong ScreenshotDataSize = 0x384000;
        private const ulong ApplicationDataSize = 0x404;

        public IScreenShotApplicationService(ServiceCtx context)
        {
            _ = context;
        }
        [CommandCmif(32)] // 7.0.0+
        // SetShimLibraryVersion(pid, u64, nn::applet::AppletResourceUserId)
        public ResultCode SetShimLibraryVersion(ServiceCtx context)
        {
            return context.Device.System.CaptureManager.SetShimLibraryVersion(context);
        }

        [CommandCmif(203)]
        // SaveScreenShotEx0(bytes<0x40> ScreenShotAttribute, u32 unknown, u64 AppletResourceUserId, pid, buffer<bytes, 0x45> ScreenshotData) -> bytes<0x20> ApplicationAlbumEntry
        public ResultCode SaveScreenShotEx0(ServiceCtx context)
        {
            // TODO: Use the ScreenShotAttribute.
#pragma warning disable IDE0059 // Remove unnecessary value assignment
            ScreenShotAttribute screenShotAttribute = context.RequestData.ReadStruct<ScreenShotAttribute>();

            uint unknown = context.RequestData.ReadUInt32();
#pragma warning restore IDE0059
            ulong appletResourceUserId = context.RequestData.ReadUInt64();
#pragma warning disable IDE0059 // Remove unnecessary value assignment
            ulong pidPlaceholder = context.RequestData.ReadUInt64();
#pragma warning restore IDE0059

            ulong screenshotDataPosition = context.Request.SendBuff[0].Position;
            ulong screenshotDataSize = context.Request.SendBuff[0].Size;

            if (screenshotDataSize < ScreenshotDataSize)
            {
                Logger.Warning?.PrintMsg(
                    LogClass.ServiceCaps,
                    $"Invalid screenshot buffer size 0x{screenshotDataSize:X}; expected at least 0x{ScreenshotDataSize:X}.");

                return ResultCode.NullInputBuffer;
            }

            byte[] screenshotData = context.Memory.GetSpan(screenshotDataPosition, (int)screenshotDataSize, true).ToArray();

            ResultCode resultCode = context.Device.System.CaptureManager.SaveScreenShot(screenshotData, appletResourceUserId, context.Device.Processes.GetProcess(context.ClientProcessId).ProgramId, out ApplicationAlbumEntry applicationAlbumEntry);

            context.ResponseData.WriteStruct(applicationAlbumEntry);

            return resultCode;
        }

        [CommandCmif(205)] // 8.0.0+
        // SaveScreenShotEx1(bytes<0x40> ScreenShotAttribute, u32 unknown, u64 AppletResourceUserId, pid, buffer<bytes, 0x15> ApplicationData, buffer<bytes, 0x45> ScreenshotData) -> bytes<0x20> ApplicationAlbumEntry
        public ResultCode SaveScreenShotEx1(ServiceCtx context)
        {
            // TODO: Use the ScreenShotAttribute.
            _ = context.RequestData.ReadStruct<ScreenShotAttribute>();

            _ = context.RequestData.ReadUInt32();
            ulong appletResourceUserId = context.RequestData.ReadUInt64();

            _ = context.RequestData.ReadUInt64();

            ulong applicationDataPosition = context.Request.SendBuff[0].Position;
            ulong applicationDataSize = context.Request.SendBuff[0].Size;

            ulong screenshotDataPosition = context.Request.SendBuff[1].Position;
            ulong screenshotDataSize = context.Request.SendBuff[1].Size;

            if (applicationDataSize != ApplicationDataSize)
            {
                Logger.Warning?.PrintMsg(
                    LogClass.ServiceCaps,
                    $"Invalid ApplicationData size 0x{applicationDataSize:X}; expected 0x{ApplicationDataSize:X}.");

                return ResultCode.InvalidArgument;
            }

            if (screenshotDataSize < ScreenshotDataSize)
            {
                Logger.Warning?.PrintMsg(
                    LogClass.ServiceCaps,
                    $"Invalid screenshot buffer size 0x{screenshotDataSize:X}; expected at least 0x{ScreenshotDataSize:X}.");

                return ResultCode.NullInputBuffer;
            }

            // TODO: Parse the application data: At 0x00 it's UserData (Size of 0x400), at 0x404 it's a uint UserDataSize (Always empty for now).
            _ = context.Memory.GetSpan(applicationDataPosition, (int)applicationDataSize).ToArray();

            byte[] screenshotData = context.Memory.GetSpan(screenshotDataPosition, (int)screenshotDataSize, true).ToArray();

            ResultCode resultCode = context.Device.System.CaptureManager.SaveScreenShot(screenshotData, appletResourceUserId, context.Device.Processes.GetProcess(context.ClientProcessId).ProgramId, out ApplicationAlbumEntry applicationAlbumEntry);

            context.ResponseData.WriteStruct(applicationAlbumEntry);

            return resultCode;
        }

        [CommandCmif(210)]
        // SaveScreenShotEx2(bytes<0x40> ScreenShotAttribute, u32 unknown, u64 AppletResourceUserId, buffer<bytes, 0x15> UserIdList, buffer<bytes, 0x45> ScreenshotData) -> bytes<0x20> ApplicationAlbumEntry
        public ResultCode SaveScreenShotEx2(ServiceCtx context)
        {
            // TODO: Use the ScreenShotAttribute.
            _ = context.RequestData.ReadStruct<ScreenShotAttribute>();

            _ = context.RequestData.ReadUInt32();
            ulong appletResourceUserId = context.RequestData.ReadUInt64();

            ulong userIdListPosition = context.Request.SendBuff[0].Position;
            ulong userIdListSize = context.Request.SendBuff[0].Size;

            ulong screenshotDataPosition = context.Request.SendBuff[1].Position;
            ulong screenshotDataSize = context.Request.SendBuff[1].Size;

            if (userIdListSize != 0x88)
            {
                Logger.Warning?.PrintMsg(
                    LogClass.ServiceCaps,
                    $"Invalid UserIdList size 0x{userIdListSize:X}; expected 0x88.");
                return ResultCode.InvalidArgument;
            }

            if (screenshotDataSize < ScreenshotDataSize)
            {
                Logger.Warning?.PrintMsg(
                    LogClass.ServiceCaps,
                    $"Invalid screenshot buffer size 0x{screenshotDataSize:X}; expected at least 0x{ScreenshotDataSize:X}.");

                return ResultCode.NullInputBuffer;
            }

            // TODO: Parse the UserIdList.
            _ = context.Memory.GetSpan(userIdListPosition, (int)userIdListSize).ToArray();

            byte[] screenshotData = context.Memory.GetSpan(screenshotDataPosition, (int)screenshotDataSize, true).ToArray();

            ResultCode resultCode = context.Device.System.CaptureManager.SaveScreenShot(screenshotData, appletResourceUserId, context.Device.Processes.GetProcess(context.ClientProcessId).ProgramId, out ApplicationAlbumEntry applicationAlbumEntry);

            context.ResponseData.WriteStruct(applicationAlbumEntry);

            return resultCode;
        }
    }
}

using Ryujinx.Common.Logging;

namespace Ryujinx.HLE.HOS.Services.Notification
{
    [Service("notif:a")] // 9.0.0+
    class INotificationServicesForApplication : IpcService
    {
        public INotificationServicesForApplication(ServiceCtx context) { }
        
        // Leaving this here since I can never find it: https://switchbrew.org/wiki/Glue_services
        
        [CommandCmif(520)] // 9.0.0+
        // ListAlarmSettings(nn::arp::ApplicationCertificate) -> s32 AlarmSettingsCount
        public ResultCode ListAlarmSettings(ServiceCtx context)
        {
            // TO-DO: Currently just returns 0. Should read in an ApplicationCertificate.
            int alarmSettingsCount = 0;
            context.ResponseData.Write(alarmSettingsCount);
            return ResultCode.Success;
        }

        [CommandCmif(1000)] // 9.0.0+
        // Initialize(PID-descriptor, u64 pid_reserved)
        public ResultCode Intialize(ServiceCtx context)
        {
            ulong pid = context.Request.HandleDesc.PId;
            context.RequestData.ReadUInt64(); // pid placeholder, zero
            
            Logger.Stub?.PrintStub(LogClass.ServiceNotification, new { pid });
            return ResultCode.Success;
        }
    }
}

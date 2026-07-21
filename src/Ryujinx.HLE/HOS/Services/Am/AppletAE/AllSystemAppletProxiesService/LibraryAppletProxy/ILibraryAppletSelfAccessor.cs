using Ryujinx.Common;
using Ryujinx.Common.Logging;
using System;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.LibraryAppletProxy
{
    class ILibraryAppletSelfAccessor : IpcService
    {
        private readonly AppletStandalone _appletStandalone = new();

        public ILibraryAppletSelfAccessor(ServiceCtx context, ulong pid)
        {
            ulong programId = context.Device.Processes.GetProcess(pid).ProgramId;

            if (programId == 0x0100000000001009)
            {
                // Create MiiEdit data.
                _appletStandalone = new AppletStandalone()
                {
                    AppletId = AppletId.MiiEdit,
                    LibraryAppletMode = LibraryAppletMode.AllForeground,
                };

                byte[] miiEditInputData = new byte[0x100];
                miiEditInputData[0] = 0x03; // Hardcoded unknown value.

                _appletStandalone.InputData.Enqueue(miiEditInputData);
            }
            else
            {
                throw new NotImplementedException($"{programId} applet is not implemented.");
            }
        }

        [CommandCmif(0)]
        // PopInData() -> object<nn::am::service::IStorage>
        public ResultCode PopInData(ServiceCtx context)
        {
            byte[] appletData = _appletStandalone.InputData.Dequeue();

            if (appletData.Length == 0)
            {
                return ResultCode.NotAvailable;
            }

            MakeObject(context, new IStorage(appletData));

            return ResultCode.Success;
        }
        
        [CommandCmif(1)]
        // PushOutData(object<nn::am::service::IStorage>)
        public ResultCode PushOutData(ServiceCtx context)
        {
            IStorage appletData = GetObject<IStorage>(context, 0);
            
            if (appletData == null || appletData.Data.Length == 0) // is this necessary?
            {
                return ResultCode.NullObject;
            }
    
            _appletStandalone.InputData.Enqueue(appletData.Data);

            return ResultCode.Success;
        }
        
        [CommandCmif(10)]
        // ExitProcessAndReturn -> nn::am::service::LibraryAppletInfo
        public ResultCode ExitProcessAndReturn(ServiceCtx context)
        {
            // Exits the LibraryApplet and returns to running the title which launched this LibraryApplet (qlaunch for example).
            // On success, official sw will enter an infinite loop with sleep-thread value 86400000000000.
            // Since we don't currently support qlaunch, it's fine to stub it.
            
            Logger.Stub?.PrintStub(LogClass.Service);
            return ResultCode.Success;
        }


        [CommandCmif(11)]
        // GetLibraryAppletInfo() -> nn::am::service::LibraryAppletInfo
        public ResultCode GetLibraryAppletInfo(ServiceCtx context)
        {
            LibraryAppletInfo libraryAppletInfo = new()
            {
                AppletId = _appletStandalone.AppletId,
                LibraryAppletMode = _appletStandalone.LibraryAppletMode,
            };

            context.ResponseData.WriteStruct(libraryAppletInfo);

            return ResultCode.Success;
        }

        [CommandCmif(14)]
        // GetCallerAppletIdentityInfo() -> nn::am::service::AppletIdentityInfo
        public ResultCode GetCallerAppletIdentityInfo(ServiceCtx context)
        {
            AppletIdentifyInfo appletIdentifyInfo = new()
            {
                AppletId = AppletId.QLaunch,
                // 0x4 padding
                TitleId = 0x0100000000001000, // qlaunch systemAppletMenu title ID
            };

            context.ResponseData.WriteStruct(appletIdentifyInfo);

            return ResultCode.Success;
        }
    }
}

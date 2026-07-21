using Ryujinx.Common.Memory;
using Ryujinx.Cpu;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Hid;
using Ryujinx.HLE.HOS.Services.Hid.HidServer;
using Ryujinx.Horizon.Common;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.Nfc.Mifare.MifareManager
{
    class IMifare : IpcService
    {
        private State _state;

        private KEvent _availabilityChangeEvent;

        private CancellationTokenSource _cancelTokenSource;

        public IMifare()
        {
            _state = State.NonInitialized;
        }

        [CommandCmif(0)]
        public ResultCode Initialize(ServiceCtx context)
        {
            _state = State.Initialized;

            NfcDevice devicePlayer1 = new()
            {
                NpadIdType = NpadIdType.Player1,
                Handle = HidUtils.GetIndexFromNpadIdType(NpadIdType.Player1),
                State = NfcDeviceState.Initialized,
            };

            context.Device.System.NfcDevices.Add(devicePlayer1);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        public ResultCode Finalize(ServiceCtx context)
        {
            if (_state == State.Initialized)
            {
                _cancelTokenSource?.Cancel();

                // NOTE: All events are destroyed here.
                context.Device.System.NfcDevices.Clear();

                _state = State.NonInitialized;
            }

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        public ResultCode GetListDevices(ServiceCtx context)
        {
            if (context.Request.RecvListBuff.Count == 0)
            {
                return ResultCode.WrongArgument;
            }

            ulong outputPosition = context.Request.RecvListBuff[0].Position;
            ulong outputSize = context.Request.RecvListBuff[0].Size;

            if (context.Device.System.NfcDevices.Count == 0)
            {
                return ResultCode.DeviceNotFound;
            }

            MemoryHelper.FillWithZeros(context.Memory, outputPosition, (int)outputSize);

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                context.Memory.Write(outputPosition + ((uint)i * sizeof(long)), (uint)context.Device.System.NfcDevices[i].Handle);
            }

            context.ResponseData.Write(context.Device.System.NfcDevices.Count);

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        public ResultCode StartDetection(ServiceCtx context)
        {
            uint deviceHandle = (uint)context.RequestData.ReadUInt64();

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                if (context.Device.System.NfcDevices[i].Handle == (PlayerIndex)deviceHandle)
                {
                    context.Device.System.NfcDevices[i].State = NfcDeviceState.SearchingForTag;

                    break;
                }
            }

            _cancelTokenSource = new CancellationTokenSource();

            Task.Run(() =>
            {
                while (true)
                {
                    if (_cancelTokenSource.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
                    {
                        if (context.Device.System.NfcDevices[i].State == NfcDeviceState.TagFound)
                        {
                            context.Device.System.NfcDevices[i].SignalActivate();
                            Thread.Sleep(125); // NOTE: Simulate skylander scanning delay.

                            break;
                        }
                    }
                }
            }, _cancelTokenSource.Token);

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        public ResultCode StopDetection(ServiceCtx context)
        {
            _cancelTokenSource?.Cancel();

            uint deviceHandle = (uint)context.RequestData.ReadUInt64();

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                if (context.Device.System.NfcDevices[i].Handle == (PlayerIndex)deviceHandle)
                {
                    context.Device.System.NfcDevices[i].State = NfcDeviceState.Initialized;
                    Array.Clear(context.Device.System.NfcDevices[i].Data);
                    context.Device.System.NfcDevices[i].SignalDeactivate();

                    break;
                }
            }

            return ResultCode.Success;
        }

        [CommandCmif(5)]
        public ResultCode ReadMifare(ServiceCtx context)
        {
            if (context.Request.ReceiveBuff.Count == 0 || context.Request.SendBuff.Count == 0)
            {
                return ResultCode.WrongArgument;
            }

            uint deviceHandle = (uint)context.RequestData.ReadUInt64();

            if (context.Device.System.NfcDevices.Count == 0)
            {
                return ResultCode.DeviceNotFound;
            }

            ulong outputPosition = context.Request.ReceiveBuff[0].Position;
            ulong outputSize = context.Request.ReceiveBuff[0].Size;

            MemoryHelper.FillWithZeros(context.Memory, outputPosition, (int)outputSize);

            ulong inputPosition = context.Request.SendBuff[0].Position;
            ulong inputSize = context.Request.SendBuff[0].Size;

            byte[] readBlockParameter = new byte[inputSize];

            context.Memory.Read(inputPosition, readBlockParameter);

            var span = MemoryMarshal.Cast<byte, NfcMifareReadBlockParameter>(readBlockParameter);
            var list = new List<NfcMifareReadBlockParameter>(span.Length);

            foreach (var item in span)
                list.Add(item);

            Thread.Sleep(125 * list.Count); // NOTE: Simulate skylander scanning delay.

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                if (context.Device.System.NfcDevices[i].Handle == (PlayerIndex)deviceHandle)
                {
                    if (context.Device.System.NfcDevices[i].State == NfcDeviceState.TagRemoved)
                    {
                        return ResultCode.TagNotFound;
                    }
                    else
                    {
                        for (int p = 0; p < list.Count; p++)
                        {
                            NfcMifareReadBlockData blockData = new()
                            {
                                SectorNumber = list[p].SectorNumber,
                                Reserved = new Array7<byte>(),
                            };
                            byte[] data = new byte[16];

                            switch (list[p].SectorKey.MifareCommand)
                            {
                                case NfcMifareCommand.NfcMifareCommand_Read:
                                case NfcMifareCommand.NfcMifareCommand_AuthA:
                                    if (IsCurrentBlockKeyBlock(list[p].SectorNumber))
                                    {
                                        Array.Copy(context.Device.System.NfcDevices[i].Data, (16 * list[p].SectorNumber) + 6, data, 6, 4);
                                    }
                                    else
                                    {
                                        Array.Copy(context.Device.System.NfcDevices[i].Data, 16 * list[p].SectorNumber, data, 0, 16);
                                    }
                                    data.CopyTo(blockData.Data.AsSpan());
                                    context.Memory.Write(outputPosition + ((uint)(p * Unsafe.SizeOf<NfcMifareReadBlockData>())), blockData);
                                    break;
                            }
                        }
                    }
                }
            }

            return ResultCode.Success;
        }

        [CommandCmif(6)]
        public ResultCode WriteMifare(ServiceCtx context)
        {
            if (context.Request.SendBuff.Count == 0)
            {
                return ResultCode.WrongArgument;
            }

            uint deviceHandle = (uint)context.RequestData.ReadUInt64();

            if (context.Device.System.NfcDevices.Count == 0)
            {
                return ResultCode.DeviceNotFound;
            }

            ulong inputPosition = context.Request.SendBuff[0].Position;
            ulong inputSize = context.Request.SendBuff[0].Size;

            byte[] writeBlockParameter = new byte[inputSize];

            context.Memory.Read(inputPosition, writeBlockParameter);

            var span = MemoryMarshal.Cast<byte, NfcMifareWriteBlockParameter>(writeBlockParameter);
            var list = new List<NfcMifareWriteBlockParameter>(span.Length);

            foreach (var item in span)
                list.Add(item);

            Thread.Sleep(125 * list.Count); // NOTE: Simulate skylander scanning delay.

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                if (context.Device.System.NfcDevices[i].Handle == (PlayerIndex)deviceHandle)
                {
                    if (context.Device.System.NfcDevices[i].State == NfcDeviceState.TagRemoved)
                    {
                        return ResultCode.TagNotFound;
                    }
                    else
                    {
                        for (int p = 0; p < list.Count; p++)
                        {
                            switch (list[p].SectorKey.MifareCommand)
                            {
                                case NfcMifareCommand.NfcMifareCommand_Write:
                                case NfcMifareCommand.NfcMifareCommand_AuthA:
                                    list[p].Data.AsSpan().CopyTo(context.Device.System.NfcDevices[i].Data.AsSpan(list[p].SectorNumber * 16, 16));
                                    break;
                            }
                        }
                    }
                }
            }

            return ResultCode.Success;
        }

        [CommandCmif(7)]
        public ResultCode GetTagInfo(ServiceCtx context)
        {
            ResultCode resultCode = ResultCode.Success;

            if (context.Request.RecvListBuff.Count == 0)
            {
                return ResultCode.WrongArgument;
            }

            ulong outputPosition = context.Request.RecvListBuff[0].Position;

            context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize((uint)Marshal.SizeOf<TagInfo>());

            MemoryHelper.FillWithZeros(context.Memory, outputPosition, Marshal.SizeOf<TagInfo>());

            uint deviceHandle = (uint)context.RequestData.ReadUInt64();

            if (context.Device.System.NfcDevices.Count == 0)
            {
                return ResultCode.DeviceNotFound;
            }

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                if (context.Device.System.NfcDevices[i].Handle == (PlayerIndex)deviceHandle)
                {
                    if (context.Device.System.NfcDevices[i].State == NfcDeviceState.TagRemoved)
                    {
                        resultCode = ResultCode.TagNotFound;
                    }
                    else
                    {
                        if (context.Device.System.NfcDevices[i].State == NfcDeviceState.TagMounted || context.Device.System.NfcDevices[i].State == NfcDeviceState.TagFound)
                        {
                            TagInfo tagInfo = new()
                            {
                                UuidLength = 4,
                                Reserved1 = new Array21<byte>(),
                                Protocol = (uint)NfcProtocol.NfcProtocol_TypeA, // Type A Protocol
                                TagType = (uint)NfcTagType.NfcTagType_Mifare, // Mifare Type
                                Reserved2 = new Array6<byte>(),
                            };

                            byte[] uuid = new byte[4];

                            Array.Copy(context.Device.System.NfcDevices[i].Data, 0, uuid, 0, 4);

                            uuid.CopyTo(tagInfo.Uuid.AsSpan());

                            context.Memory.Write(outputPosition, tagInfo);

                            resultCode = ResultCode.Success;
                        }
                        else
                        {
                            resultCode = ResultCode.WrongDeviceState;
                        }
                    }

                    break;
                }
            }

            return resultCode;
        }

        [CommandCmif(8)]
        public ResultCode AttachActivateEvent(ServiceCtx context)
        {
            uint deviceHandle = (uint)context.RequestData.ReadUInt64();

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                if ((uint)context.Device.System.NfcDevices[i].Handle == deviceHandle)
                {
                    context.Device.System.NfcDevices[i].ActivateEvent = new KEvent(context.Device.System.KernelContext);

                    if (context.Process.HandleTable.GenerateHandle(context.Device.System.NfcDevices[i].ActivateEvent.ReadableEvent, out int activateEventHandle) != Result.Success)
                    {
                        throw new InvalidOperationException("Out of handles!");
                    }

                    context.Response.HandleDesc = IpcHandleDesc.MakeCopy(activateEventHandle);

                    return ResultCode.Success;
                }
            }

            return ResultCode.DeviceNotFound;
        }

        [CommandCmif(9)]
        public ResultCode AttachDeactivateEvent(ServiceCtx context)
        {
            uint deviceHandle = (uint)context.RequestData.ReadUInt64();

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                if ((uint)context.Device.System.NfcDevices[i].Handle == deviceHandle)
                {
                    context.Device.System.NfcDevices[i].DeactivateEvent = new KEvent(context.Device.System.KernelContext);

                    if (context.Process.HandleTable.GenerateHandle(context.Device.System.NfcDevices[i].DeactivateEvent.ReadableEvent, out int deactivateEventHandle) != Result.Success)
                    {
                        throw new InvalidOperationException("Out of handles!");
                    }

                    context.Response.HandleDesc = IpcHandleDesc.MakeCopy(deactivateEventHandle);

                    return ResultCode.Success;
                }
            }

            return ResultCode.DeviceNotFound;
        }

        [CommandCmif(10)]
        public ResultCode GetState(ServiceCtx context)
        {
            context.ResponseData.Write((int)_state);

            return ResultCode.Success;
        }

        [CommandCmif(11)]
        public ResultCode GetDeviceState(ServiceCtx context)
        {
            uint deviceHandle = (uint)context.RequestData.ReadUInt64();

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                if ((uint)context.Device.System.NfcDevices[i].Handle == deviceHandle)
                {
                    if (context.Device.System.NfcDevices[i].State > NfcDeviceState.Finalized)
                    {
                        throw new InvalidOperationException($"{nameof(context.Device.System.NfcDevices)} contains an invalid state for device {i}: {context.Device.System.NfcDevices[i].State}");
                    }
                    context.ResponseData.Write((uint)context.Device.System.NfcDevices[i].State);

                    return ResultCode.Success;
                }
            }

            context.ResponseData.Write((uint)NfcDeviceState.Unavailable);

            return ResultCode.DeviceNotFound;
        }

        [CommandCmif(12)]
        public ResultCode GetNpadId(ServiceCtx context)
        {
            uint deviceHandle = (uint)context.RequestData.ReadUInt64();

            for (int i = 0; i < context.Device.System.NfcDevices.Count; i++)
            {
                if ((uint)context.Device.System.NfcDevices[i].Handle == deviceHandle)
                {
                    context.ResponseData.Write((uint)HidUtils.GetNpadIdTypeFromIndex(context.Device.System.NfcDevices[i].Handle));

                    return ResultCode.Success;
                }
            }

            return ResultCode.DeviceNotFound;
        }

        [CommandCmif(13)]
        public ResultCode AttachAvailabilityChangeEvent(ServiceCtx context)
        {
            _availabilityChangeEvent = new KEvent(context.Device.System.KernelContext);

            if (context.Process.HandleTable.GenerateHandle(_availabilityChangeEvent.ReadableEvent, out int availabilityChangeEventHandle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(availabilityChangeEventHandle);

            return ResultCode.Success;
        }

        private bool IsCurrentBlockKeyBlock(byte block)
        {
            return ((block + 1) % 4) == 0;
        }
    }
}

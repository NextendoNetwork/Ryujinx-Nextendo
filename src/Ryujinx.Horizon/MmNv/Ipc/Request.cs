using Ryujinx.Common.Logging;
using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.MmNv;
using Ryujinx.Horizon.Sdk.Sf;
using System.Collections.Generic;

namespace Ryujinx.Horizon.MmNv.Ipc
{
    partial class Request : IRequest
    {
        private readonly List<Session> _sessionList = [];

        private uint _uniqueId = 1;

        [CmifCommand(0)]
        public Result InitializeOld(Module module, uint fgmPriority, uint autoClearEvent)
        {
            bool isAutoClearEvent = autoClearEvent != 0;
            uint requestId = Register(module, fgmPriority, isAutoClearEvent);

            Logger.Info?.PrintMsg(LogClass.ServiceMm, $"Initialized module={module} requestId={requestId} autoClearEvent={isAutoClearEvent}");

            return Result.Success;
        }

        [CmifCommand(1)]
        public Result FinalizeOld(Module module)
        {
            lock (_sessionList)
            {
                Session session = GetSessionByModule(module);

                if (session != null)
                {
                    _sessionList.Remove(session);
                }
            }

            Logger.Info?.PrintMsg(LogClass.ServiceMm, $"Finalized module={module}");

            return Result.Success;
        }

        [CmifCommand(2)]
        public Result SetAndWaitOld(Module module, uint clockRateMin, uint timeout)
        {
            uint actualClockRate;

            lock (_sessionList)
            {
                Session session = GetSessionByModule(module) ?? RegisterSession(module, false);
                ApplyClockRequest(session, clockRateMin, timeout);
                actualClockRate = GetEffectiveClockRate(module);
            }

            Logger.Trace?.PrintMsg(LogClass.ServiceMm, $"SetAndWait module={module} requested={clockRateMin} actual={actualClockRate} timeout={timeout}");

            return Result.Success;
        }

        [CmifCommand(3)]
        public Result GetOld(out uint clockRateActual, Module module)
        {
            lock (_sessionList)
            {
                clockRateActual = GetEffectiveClockRate(module);
            }

            Logger.Trace?.PrintMsg(LogClass.ServiceMm, $"Get module={module} actual={clockRateActual}");

            return Result.Success;
        }

        [CmifCommand(4)]
        public Result Initialize(out uint requestId, Module module, uint fgmPriority, uint autoClearEvent)
        {
            bool isAutoClearEvent = autoClearEvent != 0;

            requestId = Register(module, fgmPriority, isAutoClearEvent);

            Logger.Info?.PrintMsg(LogClass.ServiceMm, $"Initialized module={module} requestId={requestId} autoClearEvent={isAutoClearEvent}");

            return Result.Success;
        }

        [CmifCommand(5)]
        public Result Finalize(uint requestId)
        {
            lock (_sessionList)
            {
                Session session = GetSessionById(requestId);

                if (session != null)
                {
                    _sessionList.Remove(session);
                }
            }

            Logger.Info?.PrintMsg(LogClass.ServiceMm, $"Finalized requestId={requestId}");

            return Result.Success;
        }

        [CmifCommand(6)]
        public Result SetAndWait(uint requestId, uint clockRateMin, uint timeout)
        {
            uint actualClockRate = 0;
            Module? module = null;

            lock (_sessionList)
            {
                Session session = GetSessionById(requestId);

                if (session != null)
                {
                    module = session.Module;
                    ApplyClockRequest(session, clockRateMin, timeout);
                    actualClockRate = GetEffectiveClockRate(session.Module);
                }
            }

            Logger.Trace?.PrintMsg(LogClass.ServiceMm, $"SetAndWait requestId={requestId} module={module?.ToString() ?? "<missing>"} requested={clockRateMin} actual={actualClockRate} timeout={timeout}");

            return Result.Success;
        }

        [CmifCommand(7)]
        public Result Get(out uint clockRateActual, uint requestId)
        {
            Module? module = null;

            lock (_sessionList)
            {
                Session session = GetSessionById(requestId);

                if (session == null)
                {
                    clockRateActual = 0;
                }
                else
                {
                    module = session.Module;
                    clockRateActual = GetEffectiveClockRate(session.Module);
                }
            }

            Logger.Trace?.PrintMsg(LogClass.ServiceMm, $"Get requestId={requestId} module={module?.ToString() ?? "<missing>"} actual={clockRateActual}");

            return Result.Success;
        }

        private Session GetSessionById(uint id)
        {
            foreach (Session session in _sessionList)
            {
                if (session.Id == id)
                {
                    return session;
                }
            }

            return null;
        }

        private Session GetSessionByModule(Module module)
        {
            foreach (Session session in _sessionList)
            {
                if (session.Module == module)
                {
                    return session;
                }
            }

            return null;
        }

        private uint Register(Module module, uint fgmPriority, bool isAutoClearEvent)
        {
            lock (_sessionList)
            {
                // Nintendo ignores the fgm priority as the other services were deprecated.
                Session session = RegisterSession(module, isAutoClearEvent);

                return session.Id;
            }
        }

        private Session RegisterSession(Module module, bool isAutoClearEvent)
        {
            Session session = new(_uniqueId++, module, isAutoClearEvent);

            _sessionList.Add(session);

            return session;
        }

        private void ApplyClockRequest(Session session, uint requestedClockRate, uint timeout)
        {
            if (IsClockRateSentinel(requestedClockRate))
            {
                session.ClearRequest(timeout);
            }
            else
            {
                session.SetAndWait(requestedClockRate, timeout);
            }
        }

        private uint GetEffectiveClockRate(Module module)
        {
            uint clockRate = 0;

            foreach (Session session in _sessionList)
            {
                if (session.Module == module && session.HasActiveRequest && session.RequestedClockRate > clockRate)
                {
                    clockRate = session.RequestedClockRate;
                }
            }

            return clockRate;
        }

        private static bool IsClockRateSentinel(uint clockRate)
        {
            return clockRate == 0 || unchecked((int)clockRate) < 0;
        }
    }
}

using ARMeilleure.State;
using Ryujinx.Common.Logging;
using Ryujinx.Cpu;
using Ryujinx.HLE.HOS.Kernel.Common;
using Ryujinx.HLE.HOS.Kernel.Process;
using Ryujinx.HLE.HOS.Kernel.SupervisorCall;
using Ryujinx.Horizon.Common;
using Ryujinx.Memory;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Ryujinx.HLE.HOS.Kernel.Threading
{
    class KThread : KSynchronizationObject, IKFutureSchedulerObject
    {
        private const int TlsUserDisableCountOffset = 0x100;
        private const int TlsUserInterruptFlagOffset = 0x102;

        // Tls -> ThreadType
        private const int TlsThreadTypeOffsetAArch64 = 0x1F8;
        private const int TlsThreadTypeOffsetAArch32 = 0x1FC;

        // Tls -> ThreadType -> Version
        private const int TlsThreadTypeVersionOffsetAArch64 = 0x46;
        private const int TlsThreadTypeVersionOffsetAArch32 = 0x26;

        // Tls -> ThreadType (Version 0) -> ThreadNamePointer
        private const int TlsThreadTypeVersion0ThreadNamePointerOffsetAArch64 = 0x1A8;
        private const int TlsThreadTypeVersion0ThreadNamePointerOffsetAArch32 = 0xE8;

        // Tls -> ThreadType (Version 1) -> ThreadNamePointer
        private const int TlsThreadTypeThreadNamePointerOffsetAArch64 = 0x1A0;
        private const int TlsThreadTypeThreadNamePointerOffsetAArch32 = 0xE4;


        public const int MaxWaitSyncObjects = 64;

        private ManualResetEventSlim _schedulerWaitEvent;

        public ManualResetEventSlim SchedulerWaitEvent => _schedulerWaitEvent;

        public Thread HostThread { get; private set; }

        public IExecutionContext Context { get; private set; }

        public KThreadContext ThreadContext { get; private set; }

        public int DynamicPriority { get; private set; }
        public ulong AffinityMask { get; private set; }

        public ulong ThreadUid { get; private set; }
        
        public bool IsThreadNamed { get; set; }

        private long _totalTimeRunning;

        public long TotalTimeRunning => _totalTimeRunning;

        public KSynchronizationObject SignaledObj { get; set; }

        private ulong _entrypoint;
        private ThreadStart _customThreadStart;
        private bool _forcedUnschedulable;

        public bool IsSchedulable => _customThreadStart == null && !_forcedUnschedulable;

        public ulong MutexAddress { get; set; }
        public int KernelWaitersCount { get; private set; }

        public KProcess Owner { get; private set; }

        private ulong _tlsAddress;

        public ulong TlsAddress => _tlsAddress;

        public KSynchronizationObject[] WaitSyncObjects { get; }
        public int[] WaitSyncHandles { get; }

        public long LastScheduledTime { get; set; }

        public readonly LinkedListNode<KThread>[] SiblingsPerCore;

        public LinkedList<KThread> Withholder { get; set; }
        public readonly LinkedListNode<KThread> WithholderNode;

        public readonly LinkedListNode<KThread> ProcessListNode;

        private readonly LinkedList<KThread> _mutexWaiters;
        private readonly LinkedListNode<KThread> _mutexWaiterNode;

        private readonly LinkedList<KThread> _pinnedWaiters;
        private readonly LinkedListNode<KThread> _pinnedWaiterNode;

        public KThread MutexOwner { get; private set; }

        public int ThreadHandleForUserMutex { get; set; }

        private ThreadSchedState _forcePauseFlags;
        private ThreadSchedState _forcePausePermissionFlags;

        public Result ObjSyncResult { get; set; }

        public int BasePriority { get; set; }
        public int PreferredCore { get; set; }

        public int CurrentCore { get; set; }
        public int ActiveCore { get; set; }

        public bool IsPinned { get; private set; }

        private ulong _originalAffinityMask;
        private int _originalPreferredCore;
        private int _originalBasePriority;
        private int _coreMigrationDisableCount;

        public ThreadSchedState SchedFlags { get; private set; }

        private int _shallBeTerminated;

        private bool ShallBeTerminated => _shallBeTerminated != 0;

        public bool TerminationRequested => ShallBeTerminated || SchedFlags == ThreadSchedState.TerminationPending;

        public bool SyncCancelled { get; set; }
        public bool WaitingSync { get; set; }

        private int _hasExited;
        private bool _hasBeenInitialized;
        private bool _hasBeenReleased;

        public bool WaitingInArbitration { get; set; }

        private readonly Lock _activityOperationLock = new();

        internal readonly ManualResetEventSlim DebugHalt = new(false);

        public KThread(KernelContext context) : base(context)
        {
            WaitSyncObjects = new KSynchronizationObject[MaxWaitSyncObjects];
            WaitSyncHandles = new int[MaxWaitSyncObjects];

            SiblingsPerCore = new LinkedListNode<KThread>[KScheduler.CpuCoresCount];

            for (int i = 0; i < SiblingsPerCore.Length; i++)
            {
                SiblingsPerCore[i] = new LinkedListNode<KThread>(this);
            }
            
            _mutexWaiters = [];
            _pinnedWaiters = [];

            WithholderNode = new LinkedListNode<KThread>(this);
            ProcessListNode = new LinkedListNode<KThread>(this);
            _mutexWaiterNode = new LinkedListNode<KThread>(this);
            _pinnedWaiterNode = new LinkedListNode<KThread>(this);
        }

        public Result Initialize(
            ulong entrypoint,
            ulong argsPtr,
            ulong stackTop,
            int priority,
            int cpuCore,
            KProcess owner,
            ThreadType type,
            ThreadStart customThreadStart = null)
        {
            if ((uint)type > 3)
            {
                throw new ArgumentException($"Invalid thread type \"{type}\".");
            }

            PreferredCore = cpuCore;
            AffinityMask |= 1UL << cpuCore;

            SchedFlags = ThreadSchedState.None;

            ActiveCore = cpuCore;
            ObjSyncResult = KernelResult.ThreadNotStarted;
            DynamicPriority = priority;
            BasePriority = priority;
            CurrentCore = cpuCore;
            IsPinned = false;

            _entrypoint = entrypoint;
            _customThreadStart = customThreadStart;

            if (type == ThreadType.User)
            {
                if (owner.AllocateThreadLocalStorage(out _tlsAddress) != Result.Success)
                {
                    return KernelResult.OutOfMemory;
                }

                MemoryHelper.FillWithZeros(owner.CpuMemory, _tlsAddress, KTlsPageInfo.TlsEntrySize);
            }

            bool is64Bits;

            if (owner != null)
            {
                Owner = owner;

                owner.IncrementReferenceCount();
                owner.IncrementThreadCount();

                is64Bits = owner.Flags.HasFlag(ProcessCreationFlags.Is64Bit);
            }
            else
            {
                is64Bits = true;
            }

            HostThread = new Thread(ThreadStart) { Name = "HLE.KThread" };

            Context = owner?.CreateExecutionContext() ?? new ProcessExecutionContext();

            ThreadContext = new KThreadContext(Context);

            Context.IsAarch32 = !is64Bits;

            Context.SetX(0, argsPtr);

            if (is64Bits)
            {
                Context.SetX(18, KSystemControl.GenerateRandom() | 1);
                Context.SetX(31, stackTop);
            }
            else
            {
                Context.SetX(13, (uint)stackTop);
            }

            Context.TpidrroEl0 = (long)_tlsAddress;
            Context.DebugPc = _entrypoint;

            ThreadUid = KernelContext.NewThreadUid();
            Context.ThreadUid = ThreadUid;

            HostThread.Name = customThreadStart != null ? $"HLE.OsThread.{ThreadUid}" : $"HLE.GuestThread.{ThreadUid}";

            _hasBeenInitialized = true;

            _forcePausePermissionFlags = ThreadSchedState.ForcePauseMask;

            if (owner != null)
            {
                owner.AddThread(this);

                if (owner.IsPaused)
                {
                    KernelContext.CriticalSection.Enter();

                    if (TerminationRequested)
                    {
                        KernelContext.CriticalSection.Leave();

                        return Result.Success;
                    }

                    _forcePauseFlags |= ThreadSchedState.ProcessPauseFlag;

                    CombineForcePauseFlags();

                    KernelContext.CriticalSection.Leave();
                }
            }

            return Result.Success;
        }

        public Result Start()
        {
            if (!KernelContext.KernelInitialized)
            {
                KernelContext.CriticalSection.Enter();

                if (!TerminationRequested)
                {
                    _forcePauseFlags |= ThreadSchedState.KernelInitPauseFlag;

                    CombineForcePauseFlags();
                }

                KernelContext.CriticalSection.Leave();
            }

            Result result = KernelResult.ThreadTerminating;

            KernelContext.CriticalSection.Enter();

            if (!ShallBeTerminated)
            {
                KThread currentThread = KernelStatic.GetCurrentThread();

                while (SchedFlags != ThreadSchedState.TerminationPending && (currentThread == null || !currentThread.TerminationRequested))
                {
                    if ((SchedFlags & ThreadSchedState.LowMask) != ThreadSchedState.None)
                    {
                        result = KernelResult.InvalidState;
                        break;
                    }

                    if (currentThread == null || currentThread._forcePauseFlags == ThreadSchedState.None)
                    {
                        if (Owner != null && _forcePauseFlags != ThreadSchedState.None)
                        {
                            CombineForcePauseFlags();
                        }

                        SetNewSchedFlags(ThreadSchedState.Running);

                        StartHostThread();

                        result = Result.Success;
                        break;
                    }
                    else
                    {
                        currentThread.CombineForcePauseFlags();

                        KernelContext.CriticalSection.Leave();
                        KernelContext.CriticalSection.Enter();

                        if (currentThread.ShallBeTerminated)
                        {
                            break;
                        }
                    }
                }
            }

            KernelContext.CriticalSection.Leave();

            return result;
        }

        public ThreadSchedState PrepareForTermination()
        {
            KernelContext.CriticalSection.Enter();

            KThread currentThread = KernelStatic.GetCurrentThread();

            if (Owner != null && currentThread != null && Owner.PinnedThreads[currentThread.CurrentCore] == this)
            {
                Owner.UnpinThread(this);
            }

            ThreadSchedState result;

            if (Interlocked.Exchange(ref _shallBeTerminated, 1) == 0)
            {
                if ((SchedFlags & ThreadSchedState.LowMask) == ThreadSchedState.None)
                {
                    SchedFlags = ThreadSchedState.TerminationPending;
                }
                else
                {
                    if (_forcePauseFlags != ThreadSchedState.None)
                    {
                        _forcePauseFlags &= ~ThreadSchedState.ThreadPauseFlag;

                        ThreadSchedState oldSchedFlags = SchedFlags;

                        SchedFlags &= ThreadSchedState.LowMask;

                        AdjustScheduling(oldSchedFlags);
                    }

                    if (BasePriority >= 0x10)
                    {
                        SetPriority(0xF);
                    }

                    if ((SchedFlags & ThreadSchedState.LowMask) == ThreadSchedState.Running)
                    {
                        // TODO: GIC distributor stuffs (sgir changes ect)
                        Context.RequestInterrupt();
                    }

                    SignaledObj = null;
                    ObjSyncResult = KernelResult.ThreadTerminating;

                    ReleaseAndResume();
                }
            }

            result = SchedFlags;

            KernelContext.CriticalSection.Leave();

            return result & ThreadSchedState.LowMask;
        }

        public void Terminate()
        {
            ThreadSchedState state = PrepareForTermination();

            if (KernelStatic.GetCurrentThread() == this && state != ThreadSchedState.TerminationPending)
            {
                KernelContext.Synchronization.WaitFor(new KSynchronizationObject[] { this }, -1, out _);
            }
        }

        public void HandlePostSyscall()
        {
            ThreadSchedState state;

            do
            {
                if (TerminationRequested)
                {
                    Exit();

                    // As the death of the thread is handled by the CPU emulator, we differ from the official kernel and return here.
                    break;
                }

                KernelContext.CriticalSection.Enter();

                if (TerminationRequested)
                {
                    state = ThreadSchedState.TerminationPending;
                }
                else
                {
                    if (_forcePauseFlags != ThreadSchedState.None)
                    {
                        CombineForcePauseFlags();
                    }

                    state = ThreadSchedState.Running;
                }

                KernelContext.CriticalSection.Leave();
            } while (state == ThreadSchedState.TerminationPending);
        }

        public void Exit()
        {
            // TODO: Debug event.

            if (Owner != null)
            {
                Owner.ResourceLimit?.Release(LimitableResource.Thread, 0, 1);

                _hasBeenReleased = true;
            }

            KernelContext.CriticalSection.Enter();

            _forcePauseFlags &= ~ThreadSchedState.ForcePauseMask;
            _forcePausePermissionFlags = 0;

            bool decRef = ExitImpl();

            Context.StopRunning();

            KernelContext.CriticalSection.Leave();

            if (decRef)
            {
                DecrementReferenceCount();
            }
        }

        private bool ExitImpl()
        {
            KernelContext.CriticalSection.Enter();

            SetNewSchedFlags(ThreadSchedState.TerminationPending);

            bool decRef = Interlocked.Exchange(ref _hasExited, 1) == 0;

            Signal();

            KernelContext.CriticalSection.Leave();

            return decRef;
        }

        private int GetEffectiveRunningCore()
        {
            for (int coreNumber = 0; coreNumber < KScheduler.CpuCoresCount; coreNumber++)
            {
                if (KernelContext.Schedulers[coreNumber].CurrentThread == this)
                {
                    return coreNumber;
                }
            }

            return -1;
        }

        public Result Sleep(long timeout)
        {
            KernelContext.CriticalSection.Enter();

            if (TerminationRequested)
            {
                KernelContext.CriticalSection.Leave();

                return KernelResult.ThreadTerminating;
            }

            SetNewSchedFlags(ThreadSchedState.Paused);

            if (timeout > 0)
            {
                KernelContext.TimeManager.ScheduleFutureInvocation(this, timeout);
            }

            KernelContext.CriticalSection.Leave();

            if (timeout > 0)
            {
                KernelContext.TimeManager.UnscheduleFutureInvocation(this);
            }

            return Result.Success;
        }

        public void SetPriority(int priority)
        {
            KernelContext.CriticalSection.Enter();

            if (IsPinned)
            {
                _originalBasePriority = priority;
            }
            else
            {
                BasePriority = priority;
            }

            UpdatePriorityInheritance();

            KernelContext.CriticalSection.Leave();
        }

        public void Suspend(ThreadSchedState type)
        {
            _forcePauseFlags |= type;

            CombineForcePauseFlags();
        }

        public void Resume(ThreadSchedState type)
        {
            ThreadSchedState oldForcePauseFlags = _forcePauseFlags;

            _forcePauseFlags &= ~type;

            if ((oldForcePauseFlags & ~type) == ThreadSchedState.None)
            {
                ThreadSchedState oldSchedFlags = SchedFlags;

                SchedFlags &= ThreadSchedState.LowMask;

                AdjustScheduling(oldSchedFlags);
            }
        }

        public Result SetActivity(bool pause)
        {
            lock (_activityOperationLock)
            {
                Result result = Result.Success;

                KernelContext.CriticalSection.Enter();

                ThreadSchedState lowNibble = SchedFlags & ThreadSchedState.LowMask;

                if (lowNibble is not ThreadSchedState.Paused and not ThreadSchedState.Running)
                {
                    KernelContext.CriticalSection.Leave();

                    return KernelResult.InvalidState;
                }

                if (!TerminationRequested)
                {
                    if (pause)
                    {
                        // Pause, the force pause flag should be clear (thread is NOT paused).
                        if ((_forcePauseFlags & ThreadSchedState.ThreadPauseFlag) == 0)
                        {
                            Suspend(ThreadSchedState.ThreadPauseFlag);
                        }
                        else
                        {
                            result = KernelResult.InvalidState;
                        }
                    }
                    else
                    {
                        // Unpause, the force pause flag should be set (thread is paused).
                        if ((_forcePauseFlags & ThreadSchedState.ThreadPauseFlag) != 0)
                        {
                            Resume(ThreadSchedState.ThreadPauseFlag);
                        }
                        else
                        {
                            result = KernelResult.InvalidState;
                        }
                    }
                }

                KernelContext.CriticalSection.Leave();

                if (result == Result.Success && pause)
                {
                    bool isThreadRunning = true;

                    while (isThreadRunning)
                    {
                        KernelContext.CriticalSection.Enter();

                        if (TerminationRequested)
                        {
                            KernelContext.CriticalSection.Leave();

                            break;
                        }

                        isThreadRunning = false;

                        if (IsPinned)
                        {
                            KThread currentThread = KernelStatic.GetCurrentThread();

                            if (currentThread.TerminationRequested)
                            {
                                KernelContext.CriticalSection.Leave();

                                result = KernelResult.ThreadTerminating;

                                break;
                            }

                            _pinnedWaiters.AddLast(_pinnedWaiterNode);

                            currentThread.Reschedule(ThreadSchedState.Paused);
                        }
                        else
                        {
                            isThreadRunning = GetEffectiveRunningCore() >= 0;
                        }

                        KernelContext.CriticalSection.Leave();
                    }
                }

                return result;
            }
        }

        public Result GetThreadContext3(out ThreadContext context)
        {
            context = default;

            lock (_activityOperationLock)
            {
                KernelContext.CriticalSection.Enter();

                if ((_forcePauseFlags & ThreadSchedState.ThreadPauseFlag) == 0)
                {
                    KernelContext.CriticalSection.Leave();

                    return KernelResult.InvalidState;
                }

                if (!TerminationRequested)
                {
                    context = GetCurrentContext();
                }

                KernelContext.CriticalSection.Leave();
            }

            return Result.Success;
        }

        private static uint GetPsr(IExecutionContext context)
        {
            return context.Pstate & 0xFF0FFE20;
        }

        private const long ContextCacheDurationTicks = 800_000; // ~0.8ms cache lifetime

        private ThreadContext _cachedContext;
        private long _contextCacheTimestamp;
        private bool _hasValidContextCache;

        private ThreadContext GetCurrentContext()
        {
            var now = DateTime.UtcNow.Ticks;

            // Cache hit
            if (_hasValidContextCache && (now - _contextCacheTimestamp) < ContextCacheDurationTicks)
            {
                return _cachedContext;
            }

            const int MaxRegistersAArch32 = 15;
            const int MaxFpuRegistersAArch32 = 16;

            ThreadContext context = new();
            Span<ulong> registersSpan = context.Registers.AsSpan();
            Span<V128> fpuRegistersSpan = context.FpuRegisters.AsSpan();

            if (Owner.Flags.HasFlag(ProcessCreationFlags.Is64Bit))
            {
                for (int i = 0; i < registersSpan.Length; i++)
                {
                    registersSpan[i] = Context.GetX(i);
                }
                for (int i = 0; i < fpuRegistersSpan.Length; i++)
                {
                    fpuRegistersSpan[i] = Context.GetV(i);
                }
                context.Fp = Context.GetX(29);
                context.Lr = Context.GetX(30);
                context.Sp = Context.GetX(31);
                context.Pc = Context.Pc;
                context.Pstate = GetPsr(Context);
                context.Tpidr = (ulong)Context.TpidrroEl0;
            }
            else
            {
                for (int i = 0; i < MaxRegistersAArch32; i++)
                {
                    registersSpan[i] = (uint)Context.GetX(i);
                }
                for (int i = 0; i < MaxFpuRegistersAArch32; i++)
                {
                    fpuRegistersSpan[i] = Context.GetV(i);
                }
                context.Pc = (uint)Context.Pc;
                context.Pstate = GetPsr(Context);
                context.Tpidr = (uint)Context.TpidrroEl0;
            }

            context.Fpcr = (uint)Context.Fpcr;
            context.Fpsr = (uint)Context.Fpsr;

            // Update cache
            _cachedContext = context;
            _contextCacheTimestamp = now;
            _hasValidContextCache = true;

            return context;
        }

        public void CancelSynchronization()
        {
            KernelContext.CriticalSection.Enter();

            if ((SchedFlags & ThreadSchedState.LowMask) != ThreadSchedState.Paused || !WaitingSync)
            {
                SyncCancelled = true;
            }
            else if (Withholder != null)
            {
                Withholder.Remove(WithholderNode);

                SetNewSchedFlags(ThreadSchedState.Running);

                Withholder = null;

                SyncCancelled = true;
            }
            else
            {
                SignaledObj = null;
                ObjSyncResult = KernelResult.Cancelled;

                SetNewSchedFlags(ThreadSchedState.Running);

                SyncCancelled = false;
            }

            KernelContext.CriticalSection.Leave();
        }

        public Result SetCoreAndAffinityMask(int newCore, ulong newAffinityMask)
        {
            lock (_activityOperationLock)
            {
                KernelContext.CriticalSection.Enter();

                bool isCoreMigrationDisabled = _coreMigrationDisableCount != 0;

                // The value -3 is "do not change the preferred core".
                if (newCore == -3)
                {
                    newCore = isCoreMigrationDisabled ? _originalPreferredCore : PreferredCore;

                    if ((newAffinityMask & (1UL << newCore)) == 0)
                    {
                        KernelContext.CriticalSection.Leave();

                        return KernelResult.InvalidCombination;
                    }
                }

                if (isCoreMigrationDisabled)
                {
                    _originalPreferredCore = newCore;
                    _originalAffinityMask = newAffinityMask;
                }
                else
                {
                    ulong oldAffinityMask = AffinityMask;

                    PreferredCore = newCore;
                    AffinityMask = newAffinityMask;

                    if (oldAffinityMask != newAffinityMask)
                    {
                        int oldCore = ActiveCore;

                        if (oldCore >= 0 && ((AffinityMask >> oldCore) & 1) == 0)
                        {
                            if (PreferredCore < 0)
                            {
                                ActiveCore = sizeof(ulong) * 8 - 1 - BitOperations.LeadingZeroCount(AffinityMask);
                            }
                            else
                            {
                                ActiveCore = PreferredCore;
                            }
                        }

                        AdjustSchedulingForNewAffinity(oldAffinityMask, oldCore);
                    }
                }

                KernelContext.CriticalSection.Leave();

                bool targetThreadPinned = true;

                while (targetThreadPinned)
                {
                    KernelContext.CriticalSection.Enter();

                    if (TerminationRequested)
                    {
                        KernelContext.CriticalSection.Leave();

                        break;
                    }

                    targetThreadPinned = false;

                    int coreNumber = GetEffectiveRunningCore();
                    bool isPinnedThreadCurrentlyRunning = coreNumber >= 0;

                    if (isPinnedThreadCurrentlyRunning && ((1UL << coreNumber) & AffinityMask) == 0)
                    {
                        if (IsPinned)
                        {
                            KThread currentThread = KernelStatic.GetCurrentThread();

                            if (currentThread.TerminationRequested)
                            {
                                KernelContext.CriticalSection.Leave();

                                return KernelResult.ThreadTerminating;
                            }

                            _pinnedWaiters.AddLast(_pinnedWaiterNode);

                            currentThread.Reschedule(ThreadSchedState.Paused);
                        }
                        else
                        {
                            targetThreadPinned = true;
                        }
                    }

                    KernelContext.CriticalSection.Leave();
                }

                return Result.Success;
            }
        }

        private void CombineForcePauseFlags()
        {
            ThreadSchedState oldFlags = SchedFlags;
            ThreadSchedState lowNibble = SchedFlags & ThreadSchedState.LowMask;

            SchedFlags = lowNibble | (_forcePauseFlags & _forcePausePermissionFlags);

            AdjustScheduling(oldFlags);
        }

        private void SetNewSchedFlags(ThreadSchedState newFlags)
        {
            KernelContext.CriticalSection.Enter();

            ThreadSchedState oldFlags = SchedFlags;

            SchedFlags = (oldFlags & ThreadSchedState.HighMask) | newFlags;

            if ((oldFlags & ThreadSchedState.LowMask) != newFlags)
            {
                AdjustScheduling(oldFlags);
            }

            KernelContext.CriticalSection.Leave();
        }

        public void ReleaseAndResume()
        {
            KernelContext.CriticalSection.Enter();

            if ((SchedFlags & ThreadSchedState.LowMask) == ThreadSchedState.Paused)
            {
                if (Withholder != null)
                {
                    Withholder.Remove(WithholderNode);

                    SetNewSchedFlags(ThreadSchedState.Running);

                    Withholder = null;
                }
                else
                {
                    SetNewSchedFlags(ThreadSchedState.Running);
                }
            }

            KernelContext.CriticalSection.Leave();
        }

        public void Reschedule(ThreadSchedState newFlags)
        {
            KernelContext.CriticalSection.Enter();

            ThreadSchedState oldFlags = SchedFlags;

            SchedFlags = (oldFlags & ThreadSchedState.HighMask) |
                         (newFlags & ThreadSchedState.LowMask);

            AdjustScheduling(oldFlags);

            KernelContext.CriticalSection.Leave();
        }

        public void AddMutexWaiter(KThread requester)
        {
            AddToMutexWaitersList(requester);

            requester.MutexOwner = this;

            UpdatePriorityInheritance();
        }

        public void RemoveMutexWaiter(KThread thread)
        {
            if (thread._mutexWaiterNode?.List != null)
            {
                _mutexWaiters.Remove(thread._mutexWaiterNode);
            }

            thread.MutexOwner = null;

            UpdatePriorityInheritance();
        }

        public KThread RelinquishMutex(ulong mutexAddress, out int count)
        {
            count = 0;

            if (_mutexWaiters.First == null)
            {
                return null;
            }

            KThread newMutexOwner = null;

            LinkedListNode<KThread> currentNode = _mutexWaiters.First;

            do
            {
                // Skip all threads that are not waiting for this mutex.
                while (currentNode != null && currentNode.Value.MutexAddress != mutexAddress)
                {
                    currentNode = currentNode.Next;
                }

                if (currentNode == null)
                {
                    break;
                }

                LinkedListNode<KThread> nextNode = currentNode.Next;

                _mutexWaiters.Remove(currentNode);

                currentNode.Value.MutexOwner = newMutexOwner;

                if (newMutexOwner != null)
                {
                    // New owner was already selected, re-insert on new owner list.
                    newMutexOwner.AddToMutexWaitersList(currentNode.Value);
                }
                else
                {
                    // New owner not selected yet, use current thread.
                    newMutexOwner = currentNode.Value;
                }

                count++;

                currentNode = nextNode;
            }
            while (currentNode != null);

            if (newMutexOwner != null)
            {
                UpdatePriorityInheritance();

                newMutexOwner.UpdatePriorityInheritance();
            }

            return newMutexOwner;
        }

        private void UpdatePriorityInheritance()
        {
            // If any of the threads waiting for the mutex has
            // higher priority than the current thread, then
            // the current thread inherits that priority.
            int highestPriority = BasePriority;

            if (_mutexWaiters.First != null)
            {
                int waitingDynamicPriority = _mutexWaiters.First.Value.DynamicPriority;

                if (waitingDynamicPriority < highestPriority)
                {
                    highestPriority = waitingDynamicPriority;
                }
            }

            if (highestPriority != DynamicPriority)
            {
                int oldPriority = DynamicPriority;

                DynamicPriority = highestPriority;

                AdjustSchedulingForNewPriority(oldPriority);

                if (MutexOwner != null)
                {
                    // Remove and re-insert to ensure proper sorting based on new priority.
                    MutexOwner._mutexWaiters.Remove(_mutexWaiterNode);

                    MutexOwner.AddToMutexWaitersList(this);

                    MutexOwner.UpdatePriorityInheritance();
                }
            }
        }

        private void AddToMutexWaitersList(KThread thread)
        {
            LinkedListNode<KThread> nextPrio = _mutexWaiters.First;

            int currentPriority = thread.DynamicPriority;

            while (nextPrio != null && nextPrio.Value.DynamicPriority <= currentPriority)
            {
                nextPrio = nextPrio.Next;
            }

            if (nextPrio != null)
            {
                _mutexWaiters.AddBefore(nextPrio, thread._mutexWaiterNode);
            }
            else
            {
                _mutexWaiters.AddLast(thread._mutexWaiterNode);
            }
        }

        private void AdjustScheduling(ThreadSchedState oldFlags)
        {
            if (oldFlags == SchedFlags)
            {
                return;
            }

            if (!IsSchedulable)
            {
                if (!_forcedUnschedulable)
                {
                    // Ensure our thread is running and we have an event.
                    StartHostThread();

                    // If the thread is not schedulable, we want to just run or pause
                    // it directly as we don't care about priority or the core it is
                    // running on in this case.

                    if (SchedFlags == ThreadSchedState.Running)
                    {
                        _schedulerWaitEvent.Set();
                    }
                    else
                    {
                        _schedulerWaitEvent.Reset();
                    }
                }

                return;
            }

            if (oldFlags == ThreadSchedState.Running)
            {
                // Was running, now it's stopped.
                if (ActiveCore >= 0)
                {
                    KernelContext.PriorityQueue.Unschedule(DynamicPriority, ActiveCore, this);
                }

                for (int core = 0; core < KScheduler.CpuCoresCount; core++)
                {
                    if (core != ActiveCore && ((AffinityMask >> core) & 1) != 0)
                    {
                        KernelContext.PriorityQueue.Unsuggest(DynamicPriority, core, this);
                    }
                }
            }
            else if (SchedFlags == ThreadSchedState.Running)
            {
                // Was stopped, now it's running.
                if (ActiveCore >= 0)
                {
                    KernelContext.PriorityQueue.Schedule(DynamicPriority, ActiveCore, this);
                }

                for (int core = 0; core < KScheduler.CpuCoresCount; core++)
                {
                    if (core != ActiveCore && ((AffinityMask >> core) & 1) != 0)
                    {
                        KernelContext.PriorityQueue.Suggest(DynamicPriority, core, this);
                    }
                }
            }

            KernelContext.ThreadReselectionRequested = true;
        }

        private void AdjustSchedulingForNewPriority(int oldPriority)
        {
            if (SchedFlags != ThreadSchedState.Running || !IsSchedulable)
            {
                return;
            }

            // Remove thread from the old priority queues.
            if (ActiveCore >= 0)
            {
                KernelContext.PriorityQueue.Unschedule(oldPriority, ActiveCore, this);
            }

            for (int core = 0; core < KScheduler.CpuCoresCount; core++)
            {
                if (core != ActiveCore && ((AffinityMask >> core) & 1) != 0)
                {
                    KernelContext.PriorityQueue.Unsuggest(oldPriority, core, this);
                }
            }

            // Add thread to the new priority queues.
            KThread currentThread = KernelStatic.GetCurrentThread();

            if (ActiveCore >= 0)
            {
                if (currentThread == this)
                {
                    KernelContext.PriorityQueue.SchedulePrepend(DynamicPriority, ActiveCore, this);
                }
                else
                {
                    KernelContext.PriorityQueue.Schedule(DynamicPriority, ActiveCore, this);
                }
            }

            for (int core = 0; core < KScheduler.CpuCoresCount; core++)
            {
                if (core != ActiveCore && ((AffinityMask >> core) & 1) != 0)
                {
                    KernelContext.PriorityQueue.Suggest(DynamicPriority, core, this);
                }
            }

            KernelContext.ThreadReselectionRequested = true;
        }

        private void AdjustSchedulingForNewAffinity(ulong oldAffinityMask, int oldCore)
        {
            if (SchedFlags != ThreadSchedState.Running || DynamicPriority >= KScheduler.PrioritiesCount || !IsSchedulable)
            {
                return;
            }

            // Remove thread from the old priority queues.
            for (int core = 0; core < KScheduler.CpuCoresCount; core++)
            {
                if (((oldAffinityMask >> core) & 1) != 0)
                {
                    if (core == oldCore)
                    {
                        KernelContext.PriorityQueue.Unschedule(DynamicPriority, core, this);
                    }
                    else
                    {
                        KernelContext.PriorityQueue.Unsuggest(DynamicPriority, core, this);
                    }
                }
            }

            // Add thread to the new priority queues.
            for (int core = 0; core < KScheduler.CpuCoresCount; core++)
            {
                if (((AffinityMask >> core) & 1) != 0)
                {
                    if (core == ActiveCore)
                    {
                        KernelContext.PriorityQueue.Schedule(DynamicPriority, core, this);
                    }
                    else
                    {
                        KernelContext.PriorityQueue.Suggest(DynamicPriority, core, this);
                    }
                }
            }

            KernelContext.ThreadReselectionRequested = true;
        }

        public void SetEntryArguments(long argsPtr, int threadHandle)
        {
            Context.SetX(0, (ulong)argsPtr);
            Context.SetX(1, (ulong)threadHandle);
        }

        public void TimeUp()
        {
            ReleaseAndResume();
        }

        public string GetGuestStackTrace()
        {
            return Owner.Debugger.GetGuestStackTrace(this);
        }

        public string GetGuestRegisterPrintout()
        {
            return Owner.Debugger.GetCpuRegisterPrintout(this);
        }

        public void PrintGuestStackTrace()
        {
            Logger.Info?.Print(LogClass.Cpu, $"Guest stack trace:\n{GetGuestStackTrace()}\n");
        }

        public void PrintGuestRegisterPrintout()
        {
            Logger.Info?.Print(LogClass.Cpu, $"Guest CPU registers:\n{GetGuestRegisterPrintout()}\n");
        }

        public void AddCpuTime(long ticks)
        {
            Interlocked.Add(ref _totalTimeRunning, ticks);
        }

        public void StartHostThread()
        {
            if (_schedulerWaitEvent == null)
            {
                ManualResetEventSlim schedulerWaitEvent = new(false);

                if (Interlocked.Exchange(ref _schedulerWaitEvent, schedulerWaitEvent) == null)
                {
                    HostThread.Start();
                }
                else
                {
                    schedulerWaitEvent.Dispose();
                }
            }
        }

        private void ThreadStart()
        {
            _schedulerWaitEvent.Wait();
            DebugHalt.Reset();
            KernelStatic.SetKernelContext(KernelContext, this);

            if (_customThreadStart != null)
            {
                _customThreadStart();

                // Ensure that anything trying to join the HLE thread is unblocked.
                Exit();
                HandlePostSyscall();
            }
            else
            {
                Owner.Context.Execute(Context, _entrypoint);
            }

            Context.Dispose();
            _schedulerWaitEvent.Dispose();
        }

        public void MakeUnschedulable()
        {
            _forcedUnschedulable = true;
        }

        public override bool IsSignaled()
        {
            return _hasExited != 0;
        }

        protected override void Destroy()
        {
            if (_hasBeenInitialized)
            {
                FreeResources();

                bool released = Owner != null || _hasBeenReleased;

                if (Owner != null)
                {
                    Owner.ResourceLimit?.Release(LimitableResource.Thread, 1, released ? 0 : 1);

                    Owner.DecrementReferenceCount();
                }
                else
                {
                    KernelContext.ResourceLimit.Release(LimitableResource.Thread, 1, released ? 0 : 1);
                }
            }
        }

        private void FreeResources()
        {
            Owner?.RemoveThread(this);

            if (_tlsAddress != 0 && Owner.FreeThreadLocalStorage(_tlsAddress) != Result.Success)
            {
                throw new InvalidOperationException("Unexpected failure freeing thread local storage.");
            }

            KernelContext.CriticalSection.Enter();

            // Wake up all threads that may be waiting for a mutex being held by this thread.
            foreach (KThread thread in _mutexWaiters)
            {
                thread.MutexOwner = null;
                thread._originalPreferredCore = 0;
                thread.ObjSyncResult = KernelResult.InvalidState;

                thread.ReleaseAndResume();
            }

            KernelContext.CriticalSection.Leave();

            Owner?.DecrementThreadCountAndTerminateIfZero();
        }

        public void Pin()
        {
            IsPinned = true;
            _coreMigrationDisableCount++;

            int activeCore = ActiveCore;

            _originalPreferredCore = PreferredCore;
            _originalAffinityMask = AffinityMask;

            ActiveCore = CurrentCore;
            PreferredCore = CurrentCore;
            AffinityMask = 1UL << CurrentCore;

            if (activeCore != CurrentCore || _originalAffinityMask != AffinityMask)
            {
                AdjustSchedulingForNewAffinity(_originalAffinityMask, activeCore);
            }

            _originalBasePriority = BasePriority;
            BasePriority = Math.Min(_originalBasePriority, BitOperations.TrailingZeroCount(Owner.Capabilities.AllowedThreadPriosMask) - 1);
            UpdatePriorityInheritance();

            // Disallows thread pausing
            _forcePausePermissionFlags &= ~ThreadSchedState.ThreadPauseFlag;
            CombineForcePauseFlags();

            // TODO: Assign reduced SVC permissions
        }

        public void Unpin()
        {
            IsPinned = false;
            _coreMigrationDisableCount--;

            ulong affinityMask = AffinityMask;
            int activeCore = ActiveCore;

            PreferredCore = _originalPreferredCore;
            AffinityMask = _originalAffinityMask;

            if (AffinityMask != affinityMask)
            {
                if ((AffinityMask & 1UL << ActiveCore) != 0)
                {
                    if (PreferredCore >= 0)
                    {
                        ActiveCore = PreferredCore;
                    }
                    else
                    {
                        ActiveCore = sizeof(ulong) * 8 - 1 - BitOperations.LeadingZeroCount((ulong)AffinityMask);
                    }

                    AdjustSchedulingForNewAffinity(affinityMask, activeCore);
                }
            }

            BasePriority = _originalBasePriority;
            UpdatePriorityInheritance();

            if (!TerminationRequested)
            {
                // Allows thread pausing
                _forcePausePermissionFlags |= ThreadSchedState.ThreadPauseFlag;
                CombineForcePauseFlags();

                // TODO: Restore SVC permissions
            }

            // Wake up waiters
            foreach (KThread waiter in _pinnedWaiters)
            {
                waiter.ReleaseAndResume();
            }

            _pinnedWaiters.Clear();
        }

        public void SynchronizePreemptionState()
        {
            KernelContext.CriticalSection.Enter();

            if (Owner != null && Owner.PinnedThreads[CurrentCore] == this)
            {
                ClearUserInterruptFlag();

                Owner.UnpinThread(this);
            }

            KernelContext.CriticalSection.Leave();
        }

        public ushort GetUserDisableCount()
        {
            return Owner.CpuMemory.Read<ushort>(_tlsAddress + TlsUserDisableCountOffset);
        }

        public void SetUserInterruptFlag()
        {
            Owner.CpuMemory.Write<ushort>(_tlsAddress + TlsUserInterruptFlagOffset, 1);
        }

        public void ClearUserInterruptFlag()
        {
            Owner.CpuMemory.Write<ushort>(_tlsAddress + TlsUserInterruptFlagOffset, 0);
        }

        public string GetThreadName()
        {
            try
            {
                ulong threadNamePtr = 0;
                if (Context.IsAarch32)
                {
                    uint threadTypePtr32 = Owner.CpuMemory.Read<uint>(_tlsAddress + TlsThreadTypeOffsetAArch32);
                    if (threadTypePtr32 == 0)
                    {
                        return "";
                    }

                    ushort version = Owner.CpuMemory.Read<ushort>(threadTypePtr32 + TlsThreadTypeVersionOffsetAArch32);
                    switch (version)
                    {
                        case 0x0000:
                        case 0xFFFF:
                            threadNamePtr = Owner.CpuMemory.Read<uint>(threadTypePtr32 + TlsThreadTypeVersion0ThreadNamePointerOffsetAArch32);
                            break;
                        case 0x0001:
                            threadNamePtr = Owner.CpuMemory.Read<uint>(threadTypePtr32 + TlsThreadTypeThreadNamePointerOffsetAArch32);
                            break;
                        default:
                            Logger.Warning?.Print(LogClass.Kernel, $"Unknown ThreadType struct version: {version}");
                            break;
                    }
                }
                else
                {
                    ulong threadTypePtr64 = Owner.CpuMemory.Read<ulong>(_tlsAddress + TlsThreadTypeOffsetAArch64);
                    if (threadTypePtr64 == 0)
                    {
                        return "";
                    }

                    ushort version = Owner.CpuMemory.Read<ushort>(threadTypePtr64 + TlsThreadTypeVersionOffsetAArch64);
                    switch (version)
                    {
                        case 0x0000:
                        case 0xFFFF:
                            threadNamePtr = Owner.CpuMemory.Read<ulong>(threadTypePtr64 + TlsThreadTypeVersion0ThreadNamePointerOffsetAArch64);
                            break;
                        case 0x0001:
                            threadNamePtr = Owner.CpuMemory.Read<ulong>(threadTypePtr64 + TlsThreadTypeThreadNamePointerOffsetAArch64);
                            break;
                        default:
                            Logger.Warning?.Print(LogClass.Kernel, $"Unknown ThreadType struct version: {version}");
                            break;
                    }
                }

                if (threadNamePtr == 0)
                {
                    return "";
                }

                List<byte> nameBytes = new();
                for (int i = 0; i < 0x20; i++)
                {
                    byte b = Owner.CpuMemory.Read<byte>(threadNamePtr + (ulong)i);
                    if (b == 0)
                    {
                        break;
                    }
                    nameBytes.Add(b);
                }
                return Encoding.UTF8.GetString(nameBytes.ToArray());

            } catch (InvalidMemoryRegionException)
            {
                Logger.Warning?.Print(LogClass.Kernel, "Failed to get thread name.");
                return "";
            } catch (Exception e) {
                Logger.Error?.Print(LogClass.Kernel, $"Error getting thread name: {e.Message}");
                return "";
            }
        }
    }
}

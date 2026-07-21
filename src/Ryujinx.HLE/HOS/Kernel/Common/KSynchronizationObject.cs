using Ryujinx.Common;
using Ryujinx.HLE.HOS.Kernel.Threading;
using System.Collections.Generic;

namespace Ryujinx.HLE.HOS.Kernel.Common
{
    class KSynchronizationObject : KAutoObject
    {
        private static readonly ObjectPool<LinkedListNode<KThread>> _nodePool = new(() => new LinkedListNode<KThread>(null));
        
        public LinkedList<KThread> WaitingThreads { get; }

        public KSynchronizationObject(KernelContext context) : base(context)
        {
            WaitingThreads = [];
        }

        public LinkedListNode<KThread> AddWaitingThread(KThread thread)
        {
            LinkedListNode<KThread> node = _nodePool.Allocate();
            node.Value = thread;
            WaitingThreads.AddLast(node);
            return node;
        }

        public void RemoveWaitingThread(LinkedListNode<KThread> node)
        {
            WaitingThreads.Remove(node);
            _nodePool.Release(node);
        }

        public virtual void Signal()
        {
            KernelContext.Synchronization.SignalObject(this);
        }

        public virtual bool IsSignaled()
        {
            return false;
        }
    }
}

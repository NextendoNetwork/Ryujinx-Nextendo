using Ryujinx.Common.Memory;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.Types
{
    [StructLayout(LayoutKind.Sequential, Size = 8)]
    struct NodeLatestUpdate
    {
        public NodeLatestUpdateFlags State;
        public Array7<byte> Reserved;
    }

    static class NodeLatestUpdateHelper
    {
        private static readonly Lock _lock = new();

        public static void CalculateLatestUpdate(this Array8<NodeLatestUpdate> array, Array8<NodeInfo> beforeNodes, Array8<NodeInfo> afterNodes)
        {
            lock (_lock)
            {
                Span<NodeLatestUpdate> arraySpan = array.AsSpan();
                Span<NodeInfo> beforeNodesSpan = beforeNodes.AsSpan();
                Span<NodeInfo> afterNodesSpan = afterNodes.AsSpan();
                
                for (int i = 0; i < 8; i++)
                {
                    if (beforeNodesSpan[i].IsConnected == 0)
                    {
                        if (afterNodesSpan[i].IsConnected != 0)
                        {
                            arraySpan[i].State |= NodeLatestUpdateFlags.Connect;
                        }
                    }
                    else
                    {
                        if (afterNodesSpan[i].IsConnected == 0)
                        {
                            arraySpan[i].State |= NodeLatestUpdateFlags.Disconnect;
                        }
                    }
                }
            }
        }

        public static NodeLatestUpdate[] ConsumeLatestUpdate(this Array8<NodeLatestUpdate> array, int number)
        {
            NodeLatestUpdate[] result = new NodeLatestUpdate[number];

            lock (_lock)
            {
                Span<NodeLatestUpdate> arraySpan = array.AsSpan();
                
                for (int i = 0; i < number; i++)
                {
                    result[i].Reserved = new Array7<byte>();

                    if (i < LdnConst.NodeCountMax)
                    {
                        result[i].State = arraySpan[i].State;
                        arraySpan[i].State = NodeLatestUpdateFlags.None;
                    }
                }
            }

            return result;
        }
    }
}

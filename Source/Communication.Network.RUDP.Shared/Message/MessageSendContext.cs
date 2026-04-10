using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Communication.Network.RUDP.Shared.Messages
{
    public class MessageSendContext
    {
        public ReliableType Reliable { get; set; }

        public MessageSendContext() { }

        public MessageSendContext(ReliableType reliable)
        {
            Reliable = reliable;
        }
    }
    public enum ReliableType : byte
    {
        //
        // 요약:
        //     Unreliable. Packets can be dropped, can be duplicated, can arrive without order.
        Unreliable = 4,
        //
        // 요약:
        //     Reliable. Packets won't be dropped, won't be duplicated, can arrive without order.
        ReliableUnordered = 0,
        //
        // 요약:
        //     Unreliable. Packets can be dropped, won't be duplicated, will arrive in order.
        Sequenced = 1,
        //
        // 요약:
        //     Reliable and ordered. Packets won't be dropped, won't be duplicated, will arrive
        //     in order.
        ReliableOrdered = 2,
        //
        // 요약:
        //     Reliable only last packet. Packets can be dropped (except the last one), won't
        //     be duplicated, will arrive in order. Cannot be fragmented
        ReliableSequenced = 3
    }
}

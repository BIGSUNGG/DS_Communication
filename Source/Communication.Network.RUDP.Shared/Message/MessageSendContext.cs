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
        public DeliveryMethod Method { get; set; }

        public MessageSendContext() { }

        public MessageSendContext(DeliveryMethod method)
        {
            Method = method;
        }
    }
}

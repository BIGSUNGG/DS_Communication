using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Communication.Network.RUDP.Shared
{
    public class SendMessageContext
    {
        public DeliveryMethod Method { get; set; }

        public SendMessageContext(DeliveryMethod method)
        {
            Method = method;
        }
    }
}

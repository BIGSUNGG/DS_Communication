using Communication.Shared.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;

namespace Communication.Shared.Sessions
{
    public interface ISession
    {
        public Task SendAsync(object message, object context);
        public Task SendAsync(object message);

        public void Disconnect();
    }
}

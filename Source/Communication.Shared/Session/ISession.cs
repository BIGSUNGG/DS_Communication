using DS.Communication.Shared.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;

namespace DS.Communication.Shared.Session
{
    public interface ISession
    {
        public Task SendAsync(object message);
        public void Disconnect();
    }
}

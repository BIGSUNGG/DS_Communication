using DS.Network.Shared.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;

namespace DS.Network.Shared.Session
{
    public interface ISession
    {
        public Task SendAsync(Message message);
        public void Disconnect();
    }
}

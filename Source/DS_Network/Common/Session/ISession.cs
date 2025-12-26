using Common.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;

namespace Common.Session
{
    public interface ISession
    {
        public Task SendAsync(Message message);
        public void Disconnect();
    }
}

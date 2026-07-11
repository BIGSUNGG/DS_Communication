using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Network
{
    public class ServerSessionManager
    {
        public static ServerSessionManager Instance => _instance.Value;
        public static Lazy<ServerSessionManager> _instance { get; set; } = new Lazy<ServerSessionManager>(() => new ServerSessionManager());

        public ServerSession? Session { get; set; }
    }
}

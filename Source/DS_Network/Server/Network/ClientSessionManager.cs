using Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class ClientSessionManager
{
    public static ClientSessionManager Instance { get; private set; } = new();

    public IEnumerable<ClientSession> Sessions => _sessions.Values;

    /// <summary>
    /// Key : SessionId, Value : ClientSession
    /// </summary>
    ConcurrentDictionary<int, ClientSession> _sessions = new();

    public void AddSession(ClientSession session)
    {
        _sessions.TryAdd(session.SessionId, session);
    }

    public void RemoveSession(ClientSession session)
    {
        _sessions.Remove(session.SessionId, out var removedSession);
    }

    public void FindSession(int sessionId, out ClientSession? session)
    {
        _sessions.TryGetValue(sessionId, out session);
    }
}
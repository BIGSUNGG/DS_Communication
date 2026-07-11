using DB.GateWay;
using Communication.Shared.Sessions;
using System.Collections.Concurrent;

namespace Server;

public sealed class AccountManager
{
    private static AccountManager? _instance;
    private static readonly object _lock = new object();

    // 로그인된 세션 관리 (Key: AccountId, Value: 로그인한 세션)
    private readonly ConcurrentDictionary<int, ISession> _loginedSessions = new();

    public ConcurrentDictionary<int, ISession> LoginedSessions => _loginedSessions;

    private AccountManager()
    {
    }

    public static AccountManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new AccountManager();
                    }
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// 로그인 처리
    /// </summary>
    /// <param name="session">로그인을 시도하는 세션</param>
    /// <param name="userId">사용자 아이디</param>
    /// <param name="password">비밀번호</param>
    /// <returns>(성공 여부, 계정 정보, 메시지)</returns>
    public (bool Success, AccountGateWay? Account, string Message) Login(ISession session, string userId, string password)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                return (false, null, "아이디와 비밀번호를 입력해주세요.");
            }

            // DB에서 사용자 조회
            AccountGateWay? account = AccountGateWay.Select(userId);

            if (account != null && account.Password == password)
            {
                // 중복 로그인 체크 및 기존 세션 연결 종료
                if (_loginedSessions.TryGetValue(account.Id, out ISession? existingSession))
                {
                    Console.WriteLine($"{(existingSession as ClientSession)?.AccountName}의 중복 로그인 확인, 먼저 접속한 클라이언트 연결 해제");

                    existingSession.Disconnect();
                    _loginedSessions.TryRemove(account.Id, out _);
                }

                // 새 세션 등록
                _loginedSessions[account.Id] = session;

                // 로그인 성공
                return (true, account, "로그인 성공");
            }
            else
            {
                // 로그인 실패
                return (false, null, "아이디 또는 비밀번호가 올바르지 않습니다.");
            }
        }
    }

    /// <summary>
    /// 회원가입 처리
    /// </summary>
    /// <param name="userId">사용자 아이디</param>
    /// <param name="password">비밀번호</param>
    /// <returns>(성공 여부, 메시지)</returns>
    public (bool Success, string Message) Register(string userId, string password)
    {
        lock (_lock)
        {
            // 입력 검증
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "아이디와 비밀번호를 입력해주세요.");
            }

            // 중복 체크
            AccountGateWay? existingAccount = AccountGateWay.Select(userId);
            if (existingAccount != null)
            {
                return (false, "이미 존재하는 아이디입니다.");
            }

            // 회원가입 처리
            AccountGateWay newAccount = new AccountGateWay
            {
                Name = userId,
                Password = password
            };

            if (newAccount.Insert())
            {
                return (true, "회원가입이 완료되었습니다.");
            }
            else
            {
                return (false, "회원가입 중 오류가 발생했습니다.");
            }
        }
    }
}


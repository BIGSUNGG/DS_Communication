using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Bot;

/// <summary>
/// 여러 BotClient 를 사용하여 서버에 대한 시나리오 테스트를 수행하는 러너
/// - 연결 테스트
/// - 중복 로그인 테스트
/// - 잘못된 로그인 테스트
/// - 회원가입 테스트
/// - 채팅 브로드캐스트 테스트
///
/// 사용 예)
///   await BotTestRunner.RunAllAsync("127.0.0.1", 7777, 5);
/// </summary>
public static class BotTestRunner
{
    /// <summary>
    /// 전체 종합 테스트 (연결/회원가입/중복 로그인/잘못된 로그인/채팅)
    /// </summary>
    public static async Task RunAllAsync(string host, int port, int botCount, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("=== BotTest 시작 ===");

        // 1. 다수 Bot 연결
        Console.ReadLine();
        var bots = await ConnectManyAsync(host, port, botCount, cancellationToken);
        if (!bots.Any())
        {
            Console.WriteLine("Bot 연결 실패, 테스트 중단.");
            return;
        }

        try
        {
            // 2. 회원가입 테스트 (각 Bot 별 다른 계정)
            Console.ReadLine();
            await RunRegisterTestAsync(bots);

            // 3. 로그인 테스트 (정상 로그인)
            Console.ReadLine();
            await RunLoginTestAsync(bots);

            // 4. 중복 로그인 테스트 (첫 Bot 계정으로 다른 Bot 로그인 시도)
            Console.ReadLine();
            await RunDuplicateLoginTestAsync(host, port, bots[0]);

            // 5. 잘못된 로그인 테스트
            Console.ReadLine();
            await RunWrongLoginTestAsync(host, port);

            // 6. 채팅 브로드캐스트 테스트
            Console.ReadLine();
            await RunChatTestAsync(bots);
        }
        finally
        {
            Console.ReadLine();
            foreach (var bot in bots)
            {
                bot.Dispose();
            }

            Console.WriteLine("=== BotTest 종료 ===");
        }
    }

    /// <summary>
    /// N개의 Bot를 생성해서 단순 연결만 확인
    /// </summary>
    public static async Task<List<BotClient>> ConnectManyAsync(string host, int port, int botCount, CancellationToken cancellationToken = default)
    {
        var bots = new List<BotClient>();

        for (int i = 0; i < botCount; i++)
        {
            var bot = new BotClient($"Bot{i + 1}", host, port);
            var connected = await bot.ConnectAsync(cancellationToken);
            if (connected)
            {
                bots.Add(bot);
            }
            else
            {
                bot.Dispose();
            }
        }

        Console.WriteLine($"연결 테스트: 요청={botCount}, 성공={bots.Count}, 실패={botCount - bots.Count}");
        return bots;
    }

    /// <summary>
    /// 각 Bot마다 서로 다른 계정으로 회원가입 요청
    /// </summary>
    public static async Task RunRegisterTestAsync(IReadOnlyList<BotClient> bots)
    {
        Console.WriteLine("=== 회원가입 테스트 시작 ===");

        var tasks = bots.Select((bot, index) =>
            bot.RegisterAsync($"bot_user_{index + 1}", "1234"));

        var results = await Task.WhenAll(tasks);

        int success = results.Count(r => r != null && r.IsSuccessful);
        int fail = results.Length - success;

        Console.WriteLine($"회원가입 테스트 결과: 성공={success}, 실패={fail}");
    }

    /// <summary>
    /// 각 Bot마다 자기 계정으로 로그인
    /// </summary>
    public static async Task RunLoginTestAsync(IReadOnlyList<BotClient> bots)
    {
        Console.WriteLine("=== 로그인 테스트 시작 ===");

        var tasks = bots.Select((bot, index) =>
            bot.LoginAsync($"bot_user_{index + 1}", "1234"));

        var results = await Task.WhenAll(tasks);

        int success = results.Count(r => r != null && r.IsSuccessful);
        int fail = results.Length - success;

        Console.WriteLine($"로그인 테스트 결과: 성공={success}, 실패={fail}");
    }

    /// <summary>
    /// 이미 사용 중인 계정으로 다른 Bot가 로그인 시도 (중복 로그인)
    /// </summary>
    public static async Task RunDuplicateLoginTestAsync(string host, int port, BotClient originalBot)
    {
        Console.WriteLine("=== 중복 로그인 테스트 시작 ===");

        // originalBot 은 이미 로그인 되어 있다고 가정 (RunLoginTestAsync 이후)
        // 같은 계정 정보로 새 Bot 생성
        using var duplicateBot = new BotClient($"{originalBot.Name}_Dup", host, port);
        if (!await duplicateBot.ConnectAsync())
        {
            Console.WriteLine("중복 로그인 테스트: 새 Bot 연결 실패");
            return;
        }

        var loginRes = await duplicateBot.LoginAsync("bot_user_1", "1234");
        if (loginRes == null)
        {
            Console.WriteLine("중복 로그인 테스트: 응답 없음");
            return;
        }

        Console.WriteLine($"중복 로그인 응답: Success={loginRes.IsSuccessful}, Message={loginRes.Message}");
    }

    /// <summary>
    /// 존재하지 않는 계정 또는 틀린 비밀번호로 로그인 시도
    /// </summary>
    public static async Task RunWrongLoginTestAsync(string host, int port)
    {
        Console.WriteLine("=== 잘못된 로그인 테스트 시작 ===");

        using var wrongBot = new BotClient("WrongLoginBot", host, port);
        if (!await wrongBot.ConnectAsync())
        {
            Console.WriteLine("잘못된 로그인 테스트: Bot 연결 실패");
            return;
        }

        // 1) 존재하지 않는 계정
        var res1 = await wrongBot.LoginAsync("not_exist_user", "1234");
        Console.WriteLine($"잘못된 로그인(없는 계정) 응답: Success={res1?.IsSuccessful}, Message={res1?.Message}");

        // 2) 잘못된 비밀번호 (회원가입 되어 있지 않을 수 있으니, 단순히 실패 여부만 확인)
        var res2 = await wrongBot.LoginAsync("bot_user_1", "wrong_password");
        Console.WriteLine($"잘못된 로그인(비밀번호 틀림) 응답: Success={res2?.IsSuccessful}, Message={res2?.Message}");
    }

    /// <summary>
    /// 로그인된 Bot들이 순서대로 채팅을 쏘고, 모든 Bot 콘솔에 브로드캐스트 로그가 찍히는지 확인
    /// </summary>
    public static async Task RunChatTestAsync(IReadOnlyList<BotClient> bots)
    {
        Console.WriteLine("=== 채팅 테스트 시작 ===");

        int index = 1;
        foreach (var bot in bots)
        {
            await bot.SendChatAsync($"Hello from {bot.Name} ({index})");
            index++;
            await Task.Delay(100); // 너무 몰아서 보내지 않도록 약간의 딜레이
        }

        Console.WriteLine("채팅 메시지 전송 완료. 서버 로그/각 Bot 콘솔 출력으로 브로드캐스트 여부를 확인하세요.");
    }
}



using Client.Bot;

namespace BotTest;

class Program
{
    static async Task Main(string[] args)
    {
        // 서버 주소와 포트 설정
        string host = "127.0.0.1";
        int port = 5000;
        int botCount = 100; // 동시에 연결할 Bot 수

        // 명령줄 인자로 설정 가능
        if (args.Length >= 1)
        {
            host = args[0];
        }
        if (args.Length >= 2 && int.TryParse(args[1], out int parsedPort))
        {
            port = parsedPort;
        }
        if (args.Length >= 3 && int.TryParse(args[2], out int parsedBotCount))
        {
            botCount = parsedBotCount;
        }

        Console.WriteLine($"=== BotTest 시작 ===");
        Console.WriteLine($"서버: {host}:{port}");
        Console.WriteLine($"Bot 수: {botCount}");
        Console.WriteLine();

        try
        {
            // 전체 테스트 실행
            await BotTestRunner.RunAllAsync(host, port, botCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"오류 발생: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine();
        Console.WriteLine("모든 Bot 테스트 완료. 엔터를 누르면 종료합니다.");
        Console.ReadLine();
    }
}


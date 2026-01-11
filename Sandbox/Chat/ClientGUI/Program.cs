using Client;
using Communication.Shared.Messages;
using Communication.Shared.Session;

namespace ClientGUI
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static async Task Main(string[] args)
        {            
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            var (host, port) = ParseArgs(args);
            Application.Run(new MainForm(host, port));
        }

        private static (string Host, int Port) ParseArgs(string[] args)
        {
            var host = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0]
                : "127.0.0.1";

            var port = 5000;
            if (args.Length > 1 && int.TryParse(args[1], out var parsed) && parsed is > 0 and < 65535)
            {
                port = parsed;
            }

            return (host, port);
        }
    }
}
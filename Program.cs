using System;
using System.Threading.Tasks;

namespace GHost3
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Check any argument position for the debug flag, not just the first.
            bool debugMode = Array.Exists(args, a => a == "-debug" || a == "--debug");

            Console.WriteLine("========================================");
            Console.WriteLine("  GHost3 Door Server for The Major BBS");
            Console.WriteLine("  Version 1.3.0\r\n");
            Console.WriteLine("  Press CTRL-C to exit");
            Console.WriteLine("========================================");
            Console.WriteLine();

            var configManager = new ConfigManager();
            var serverConfig = configManager.LoadConfig();
            var listenAddress = serverConfig.GetListenIPAddress();

            Console.WriteLine($"Configuration loaded:");
            Console.WriteLine($"  Listen Address: {GHost3Server.GetDisplayAddress(listenAddress)}");
            Console.WriteLine($"  RLogin Port: {serverConfig.RLoginPort}");
            Console.WriteLine($"  Drop File Directory: {serverConfig.DropFileDirectory}");
            Console.WriteLine($"  Max Sessions: {serverConfig.MaxSessions}");
            Console.WriteLine($"  Configured Doors: {serverConfig.Doors.Count}");
            Console.WriteLine($"  Enabled Doors: {serverConfig.Doors.FindAll(d => d.Enabled).Count}");

            if (debugMode)
            {
                Console.WriteLine($"  Debug Mode: ENABLED");
            }
            Console.WriteLine();

            if (serverConfig.Doors.FindAll(d => d.Enabled).Count == 0)
            {
                Console.WriteLine("WARNING: No doors are enabled!");
                Console.WriteLine("Edit doorserver.json to enable doors.");
                Console.WriteLine();
            }

            var doorConfig = configManager.ToDoorConfig(serverConfig);
            var server = new GHost3Server(serverConfig.RLoginPort, listenAddress, doorConfig, debugMode);

            // Handle Ctrl+C
            bool shutdownRequested = false;
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                if (!shutdownRequested)
                {
                    shutdownRequested = true;
                    Console.WriteLine("\nShutdown requested...");
                    server.Stop();
                }
            };

            try
            {
                await server.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }

            Console.WriteLine("Server stopped.");
        }
    }
}

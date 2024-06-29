using System;
using System.IO;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UsrpRouter
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: UsrpRouter <mode> <config file>");
                Console.WriteLine("Modes: router, bridge");
                return;
            }

            var mode = args[0];
            var configFilePath = args[1];

            if (mode.ToLower() == "router")
            {
                var router = new UsrpRouter(configFilePath);
                await router.StartRoutingAsync();
            }
            else if (mode.ToLower() == "bridge")
            {
                var bridge = new UsrpBridge(configFilePath);
                await bridge.StartBridgingAsync();
            }
            else
            {
                Console.WriteLine("Invalid mode specified. Use 'router' or 'bridge'.");
            }
        }
    }
}
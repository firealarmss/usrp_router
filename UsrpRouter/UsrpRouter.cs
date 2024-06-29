using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.IO;

namespace UsrpRouter
{
    public class UsrpRouter
    {
        private const int UsrpVoiceFrameSize = 160 * sizeof(short);

        private readonly string _configFilePath;
        private List<Bridge> _bridges;
        private List<RoutingRule> _routingRules;
        private List<UdpClient> _udpClients;

        public UsrpRouter(string configFilePath)
        {
            _configFilePath = configFilePath;
            LoadConfiguration();
            _udpClients = new List<UdpClient>();
        }

        private void LoadConfiguration()
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(LowerCaseNamingConvention.Instance)
                .Build();

            using (var reader = new StreamReader(_configFilePath))
            {
                var yamlObject = deserializer.Deserialize<RouterConfig>(reader);
                _bridges = yamlObject.bridges;
                _routingRules = yamlObject.routing;
            }
        }

        public async Task StartRoutingAsync()
        {
            var tasks = new List<Task>();

            foreach (var bridge in _bridges)
            {
                Console.WriteLine($"Starting bridge {bridge.name} on RX port {bridge.receiveport}");
                tasks.Add(StartUdpRoutingAsync(bridge));
            }

            await Task.WhenAll(tasks);
        }

        private async Task StartUdpRoutingAsync(Bridge bridge)
        {
            UdpClient rxClient = new UdpClient(bridge.receiveport);
            _udpClients.Add(rxClient);

            IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, bridge.receiveport);
            Console.WriteLine($"Endpoint: {remoteEndpoint.Address} : {remoteEndpoint.Port}");
            while (true)
            {
                try
                {
                    var receivedResult = await rxClient.ReceiveAsync();
                    var data = receivedResult.Buffer;

                    if (IsValidUsrpData(data))
                    {
                        if (data.Length != 32)
                            Console.WriteLine($"RX Audio from: {bridge.name}");
                        else
                            Console.WriteLine($"Call ended from: {bridge.name}");

                        var routingRule = _routingRules.Find(r => r.source == bridge.name);
                        if (routingRule != null)
                        {
                            foreach (var destination in routingRule.destinations)
                            {
                                var destinationBridge = _bridges.Find(b => b.name == destination);
                                if (destinationBridge != null)
                                {
                                    //Console.WriteLine("Sending to: " + destinationBridge.address + ":" + destinationBridge.sendport);
                                    SendDataToTxPort(data, destinationBridge.address, destinationBridge.sendport);
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Invalid USRP data received on RX port {bridge.receiveport}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error receiving data on RX port {bridge.receiveport}: {ex.Message}");
                }
            }
        }

        private bool IsValidUsrpData(byte[] data)
        {
            bool callEnd = false;

            if (data.Length == 32)
                callEnd = true;

            if (data.Length < Marshal.SizeOf(typeof(UsrpDataHeader)) + UsrpVoiceFrameSize - 1 && !callEnd)
            {
                Console.WriteLine($"Invalid size: {data.Length}");
                return false;
            }

            var header = ByteArrayToStructure<UsrpDataHeader>(data);

            if (header.eye != "USRP")
            {
                Console.WriteLine("Invalid USRP header");
                return false;
            }

            Console.WriteLine(header.ToString());

            return true;
        }

        private void SendDataToTxPort(byte[] data, string address, int txPort)
        {
            using (UdpClient txClient = new UdpClient())
            {
                IPEndPoint txEndpoint = new IPEndPoint(IPAddress.Parse(address), txPort);
                txClient.Send(data, data.Length, txEndpoint);
            }
        }

        private T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UsrpDataHeader
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
        public string eye;
        public uint seq;
        public uint memory;
        public uint keyup;
        public uint talkgroup;
        public uint type;
        public uint mpxid;
        public uint reserved;

        public override string ToString()
        {
            return $"Eye: {eye}, Seq: {seq}, Memory: {memory}, Keyup: {keyup}, Talkgroup: {talkgroup}, Type: {type}, Mpxid: {mpxid}, Reserved: {reserved}";
        }
    }

    public class Bridge
    {
        public string name { get; set; }
        public string address { get; set; }
        public int receiveport { get; set; }
        public int sendport { get; set; }
        public string type { get; set; }
    }

    public class RoutingRule
    {
        public string source { get; set; }
        public List<string> destinations { get; set; }
    }

    public class RouterConfig
    {
        public List<Bridge> bridges { get; set; }
        public List<RoutingRule> routing { get; set; }
    }
}
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
    public class UsrpBridge
    {
        private const int UsrpVoiceFrameSize = 160 * sizeof(short);

        private readonly string _configFilePath;
        private List<Bridge> _bridges;
        private List<UdpClient> _udpClients;

        public UsrpBridge(string configFilePath)
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
                var yamlObject = deserializer.Deserialize<BridgeConfig>(reader);
                _bridges = yamlObject.bridges;
            }
        }

        public async Task StartBridgingAsync()
        {
            var tasks = new List<Task>();

            foreach (var bridge in _bridges)
            {
                Console.WriteLine($"Starting bridge {bridge.name} on RX port {bridge.receiveport}");
                tasks.Add(StartUdpBridgingAsync(bridge));
            }

            await Task.WhenAll(tasks);
        }

        private async Task StartUdpBridgingAsync(Bridge bridge)
        {
            UdpClient rxClient = new UdpClient(bridge.receiveport);
            _udpClients.Add(rxClient);

            IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, bridge.receiveport);
            while (true)
            {
                try
                {
                    var receivedResult = await rxClient.ReceiveAsync();
                    var data = receivedResult.Buffer;

                    if (IsValidUsrpData(data))
                    {
                        Console.WriteLine($"Call started from: {bridge.name}");
                        foreach (var b in _bridges)
                        {
                            if (b.name != bridge.name)
                            {
                                SendDataToTxPort(data, b.address, b.sendport);
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
            if (data.Length < Marshal.SizeOf(typeof(UsrpDataHeader)) + UsrpVoiceFrameSize)
            {
                return false;
            }

            var header = ByteArrayToStructure<UsrpDataHeader>(data);

            if (header.eye != "USRP")
            {
                return false;
            }

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

    public class BridgeConfig
    {
        public List<Bridge> bridges { get; set; }
    }
}
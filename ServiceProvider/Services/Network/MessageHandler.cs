using NetworkLibrary;
using NetworkLibrary.Components;
using NetworkLibrary.P2P.Components;
using NetworkLibrary.P2P.Generic;
using ProtoBuf.Meta;
using Protobuff;
using Protobuff.P2P;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Videocall
{
    public class MessageHeaders
    {
        public const string Identify = "Who";
        public const string AudioSample = "AS";
        public const string ImageMessage = "IMG";
        public const string Text = "Text";
        public const string FileDirectoryStructure = "FileDirectoryStructure";
        public const string FileTransfer = "FileTransfer";
        public const string Call = "Call";
        public const string EndCall = "EndCall";
        public const string RemoteClosedCam = "RemoteClosedCam";
        public const string VideoAck = "Vack";
        public const string MicClosed = "MicClosed";
        public const string RequestKeyFrame = "RequestKeyFrame";
        public const string MarkikngFeedbackVH = "MFVH";
        public const string LtrRecoveryVH = "LTRVH";
        public const string MarkikngFeedbackSC = "MFSC";
        public const string LtrRecoverySC = "LTRSC";
    }
    public class MessageHandler
    {
        private static MessageHandler instance;

        public static MessageHandler Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new MessageHandler();
                }
                return instance;
            }
        }
         

        private RelayClient client;
        public ConcurrentDictionary<Guid,bool> registeredPeers = new ConcurrentDictionary<Guid,bool>();
        public Action<MessageEnvelope> OnMessageAvailable;
        public ConcurrentProtoSerialiser Serializer =  new ConcurrentProtoSerialiser();
        public Action<Guid> OnPeerRegistered;
        public Action<Guid> OnPeerUnregistered;
        public Action OnDisconnected;

        public string TransportLayer { get;  set; } = "Udp";
        public string FTTransportLayer { get; set; } = "Tcp";
        public Guid SessionId => client.SessionId;

        public MessageHandler()
        {
           InitializeClient();
        }
        private void InitializeClient()
        {
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            X509Certificate2 cert = null;
            if (File.Exists(path + "/client.pfx"))
                 cert = new X509Certificate2(path + "/client.pfx", "greenpass");
            client = new RelayClient(cert,0);
            // client.MaxUdpPackageSize = 32000;
            //client.EnableJumboUdpRateControl = true;

            client.OnMessageReceived += TcpMessageReceived;
            client.OnUdpMessageReceived += UdpMessageReceived;
            client.OnPeerRegistered += HandlePeerRegistered;
            client.OnPeerUnregistered += HandlePeerUnregistered;
            client.OnDisconnected += HandleDisconnected;
            client.StartPingService();
        }

        private void HandleDisconnected()
        {
            Task.Run(() =>
            {
                foreach (var item in registeredPeers)
                {
                    OnPeerUnregistered?.Invoke(item.Key);
                }
                registeredPeers.Clear();
                OnDisconnected?.Invoke();
            });


            //client.Dispose();
            //client = null;
            //InitializeClient();
        }

        private void HandlePeerUnregistered(Guid obj)
        {
            Task.Run(() =>
            {
                try
                {
                    registeredPeers.TryRemove(obj, out _);
                    OnPeerUnregistered?.Invoke(obj);
                }
                catch (Exception e)
                {
                    ServiceHub.Instance.Log("Error", $"Unhandled exception occured while peer unregistered:\n{e.Message}" +
                        $"\nStackTrace:{e.StackTrace}");
                }
            });
        }

        private void HandlePeerRegistered(Guid id)
        {
            Task.Run(() =>
            {
                try
                {
                    registeredPeers.TryAdd(id, true);
                    OnPeerRegistered?.Invoke(id);
                }
                catch (Exception e)
                {
                    ServiceHub.Instance.Log("Error", $"Unhandled exception occured while peer registered:\n{e.Message}" +
                        $"\nStackTrace:{e.StackTrace}");
                }
            });
           
        }

        
        private void UdpMessageReceived(MessageEnvelope message)
        {
            HandleMessage(message);
        }

        private void TcpMessageReceived(MessageEnvelope message)
        {
            HandleMessage(message);
        }

        private void HandleMessage(MessageEnvelope message)
        {
            try
            {
                OnMessageAvailable?.Invoke(message);

            }
            catch (Exception e)
            {
                ServiceHub.Instance.Log("Error", $"Unhandled exception occured while handling message:\n{e.Message}ù" +
                    $"\nStackTrace:{e.StackTrace}");
            }


        }

        public void SendStreamMessage<T>(Guid id, T message, bool reliable) where T : IProtoMessage
        {
            if (TransportLayer == "Udp")
            {
                if (reliable)
                {
                    MessageEnvelope envelope = new MessageEnvelope();
                    envelope.Header = typeof(T).Name;
                    client.SendRudpMessage(id, envelope, message);
                }
                else
                {
                    MessageEnvelope envelope = new MessageEnvelope();
                    envelope.Header = typeof(T).Name;
                    client.SendUdpMessage(id, envelope, message);
                }

            }

            else
            {
                MessageEnvelope envelope = new MessageEnvelope();
                envelope.Header = typeof(T).Name;
                client.SendAsyncMessage(id, envelope, message);
            }
        }

        public void SendStreamMessage(Guid id, MessageEnvelope message, bool reliable) 
        {
            if (id == Guid.Empty)
                return;
            if (TransportLayer == "Udp")
            {
                if(reliable)
                    client.SendRudpMessage(id, message);
                else
                    client.SendUdpMessage(id, message);
            }

            else
                client.SendAsyncMessage(id, message);
        }
        public void SendStreamMessage(Guid id, MessageEnvelope message, bool reliable, Action<PooledMemoryStream> OnBeforeSerialize)
        {
            if (id == Guid.Empty)
                return;
            if (TransportLayer == "Udp")
            {
                if (reliable)
                    //client.SendRudpMessage(id, message, OnBeforeSerialize);
                    client.SendAsyncMessage(id, message, OnBeforeSerialize);

                else
                    client.SendUdpMessage(id, message,OnBeforeSerialize);
            }
            else
                client.SendAsyncMessage(id, message, OnBeforeSerialize);
        }

        public void SendAsyncMessage(Guid peerId,MessageEnvelope envelope, bool forceTCP=false)
        {
            if (peerId == Guid.Empty)
                return;
            if (TransportLayer == "Udp" &&!forceTCP)
                client.SendRudpMessage(peerId, envelope, RudpChannel.Realtime);

            else
                client.SendAsyncMessage(peerId, envelope);
            
        }


        public void SendAsyncMessage<T>(Guid peerId, MessageEnvelope envelope, T m, bool forceTCP = false) where T : IProtoMessage
        {
            if (peerId == Guid.Empty)
                return;
            if (TransportLayer == "Udp" && !forceTCP)
                client.SendRudpMessage(peerId, envelope, m, RudpChannel.Realtime);

            else
                client.SendAsyncMessage(peerId, envelope, m);
        }

        public Task<MessageEnvelope> SendRequesAndWaitResponse(Guid peerId, MessageEnvelope envelope, int timeout = 10000, RudpChannel channel = RudpChannel.Ch1)
        {
            if (TransportLayer == "Udp")
                return client.SendRudpMessageAndWaitResponse(peerId, envelope, timeout,channel);

            else
                return client.SendRequestAndWaitResponse(peerId, envelope, timeout);
        }

        public Task<MessageEnvelope> SendRequesAndWaitResponseFT(Guid peerId, MessageEnvelope envelope, int timeout = 10000, RudpChannel channel = RudpChannel.Ch1)
        {

            if (FTTransportLayer == "Udp")
                return client.SendRudpMessageAndWaitResponse(peerId, envelope, timeout, channel);

            else
                return client.SendRequestAndWaitResponse(peerId, envelope, timeout);
        }

        public Task<MessageEnvelope> SendRequesAndWaitResponse<T>(Guid peerId, MessageEnvelope envelope, T m, int timeout = 10000, RudpChannel channel = RudpChannel.Ch1) where T : IProtoMessage
        {
            if (TransportLayer == "Udp")
                return client.SendRudpMessageAndWaitResponse(peerId, envelope, timeout,  channel);

            else
                return client.SendRequestAndWaitResponse(peerId, envelope,m, timeout);
        }

        public void SendRudpMessage(Guid peerId, MessageEnvelope message, RudpChannel channel = RudpChannel.Ch1)
        {
            client.SendRudpMessage(peerId, message,channel);
        }

        public void SendRudpMessage<T>(Guid peerId, MessageEnvelope message,T m)
        {
            client.SendRudpMessage(peerId, message,m);
        }

        public Task<MessageEnvelope> SendRudpMessageAndWaitResponse(Guid peerId,MessageEnvelope envelope, int timeOut = 10000, RudpChannel channel = RudpChannel.Ch1)
        {
            return client.SendRudpMessageAndWaitResponse(peerId, envelope,timeOut,channel);
        }

        public Task<MessageEnvelope> SendRudpMessageAndWaitResponse<T>(Guid peerId, MessageEnvelope envelope, T m, int timeOut = 10000)
        {
            return client.SendRudpMessageAndWaitResponse(peerId, envelope,m, timeOut);
        }



        public Task<MessageEnvelope> SendRequestAndWaitResponse(Guid peerId, MessageEnvelope env, int timeout, RudpChannel channel = RudpChannel.Ch1)
        {
            if (TransportLayer == "Udp")
                return client.SendRudpMessageAndWaitResponse(peerId, env, timeout,channel);

            else
                return client.SendRequestAndWaitResponse(peerId, env,timeout);
        }

        public Task<MessageEnvelope> SendRequestAndWaitResponse(Guid guid, VCPeerInfo peerInfo, string messageHeader, int timeout, RudpChannel channel = RudpChannel.Ch1)
        {
            if (TransportLayer == "Udp")
            {
                MessageEnvelope envelope = new MessageEnvelope();
                envelope.Header = messageHeader == null ? typeof(VCPeerInfo).Name : messageHeader;

                return client.SendRudpMessageAndWaitResponse(guid, envelope, peerInfo, timeout,channel);
            }
            else
                return client.SendRequestAndWaitResponse(guid, peerInfo, messageHeader, timeout);
        }

        public PeerInformation GetPeerInfo(Guid peerId)
        {
           return client.GetPeerInfo(peerId);
        }

        public Dictionary<Guid, double> GetUdpPingStatus()
        {
           return client?.GetUdpPingStatus();
        }

        public Dictionary<Guid, double> GetTcpPingStatus()
        {
            return client?.GetTcpPingStatus();
        }

        public void Disconnect()
        {
            try
            {
                foreach (var item in registeredPeers)
                {
                    OnPeerUnregistered?.Invoke(item.Key);
                }
                registeredPeers.Clear();
                client.Disconnect();
            }
            catch { }
           
            //client.Dispose();
            //client = null;
            //InitializeClient();
        }

        public Task<bool> ConnectAsync(string v1, int v2)
        {
           return client.ConnectAsync (v1, v2);
        }

        public Task<bool> RequestHolePunchAsync(Guid peerId, int v)
        {
            return client.RequestHolePunchAsync(peerId, v);
        }

        public Task<List<ServerInfo>> SearchRelayServer(int port)
        {
            return client.TryFindRelayServer(port);
        }

        public Task<bool> RequestTcpHolePunchAsync(Guid peerId, int v)
        {
            return client.RequestTcpHolePunchAsync(peerId);
        }
    }
}

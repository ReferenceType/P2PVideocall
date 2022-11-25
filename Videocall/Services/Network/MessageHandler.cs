using Protobuff;
using Protobuff.P2P;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Videocall
{
    internal class MessageHandler
    {
        public RelayClient client;
        public HashSet<Guid> registeredPeers = new HashSet<Guid>();
        public Action<MessageEnvelope> OnMessageAvailable;
        internal ConcurrentProtoSerialiser Serializer =  new ConcurrentProtoSerialiser();

        public string TransportLayer { get; internal set; } = "Udp";

        public MessageHandler()
        {
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var cert = new X509Certificate2(path + "/client.pfx", "greenpass");
            client = new RelayClient(cert);

            client.OnMessageReceived += TcpMessageReceived;
            client.OnUdpMessageReceived += UdpMessageReceived;
            client.OnPeerRegistered += HandlePeerRegistered;

            client.StartPingService();
        }

        private void HandlePeerRegistered(Guid id)
        {
           registeredPeers.Add(id);
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
            OnMessageAvailable?.Invoke(message);
          
        }

       internal void SendMessage<T>(Guid id, T message) where T : IProtoMessage
        {
            if (TransportLayer == "Udp")
                client.SendUdpMesssage(id, message);

            else
                client.SendAsyncMessage(id, message);
        }

       
    }
}

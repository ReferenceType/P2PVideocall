using ProtoBuf.Meta;
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
    public class MessageHeaders
    {
        public const string Identify = "Who";
        public const string AudioSample = "AudioSample";
        public const string ImageMessage = "ImageMessage";
        public const string Text = "Text";
        public const string FileDirectoryStructure = "FileDirectoryStructure";
        public const string FileTransfer = "FileTransfer";
        public const string Call = "Call";
        public const string EndCall = "EndCall";
        public const string RemoteClosedCam = "RemoteClosedCam";
        public const string VideoAck = "VideoAck";
        public const string MicClosed = "MicClosed";
    }
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
            client.OnPeerUnregistered += HandlePeerUnregistered;

            client.StartPingService();
        }

        private void HandlePeerUnregistered(Guid obj)
        {
            registeredPeers.Remove(obj);

        }

        private void HandlePeerRegistered(Guid id)
        {
           registeredPeers.Add(id);
            Console.WriteLine("Peer Registered");
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

       internal void SendStreamMessage<T>(Guid id, T message) where T : IProtoMessage
        {
            if (TransportLayer == "Udp")
                client.SendUdpMesssage(id, message,channel:1);

            else
                client.SendAsyncMessage(id, message);
        }
        internal void SendStreamMessage(Guid id, MessageEnvelope message) 
        {
            if (TransportLayer == "Udp")
                client.SendUdpMesssage(id, message);

            else
                client.SendAsyncMessage(id, message);
        }


    }
}

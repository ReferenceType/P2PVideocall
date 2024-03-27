using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Videocall
{
    [ProtoContract]
    public class VCPeerInfo : INotifyPropertyChanged, IProtoMessage
    {
        private double tcpLatency;
        private double udpLatency;

        public VCPeerInfo(string name, string ip, int port, Guid guid)
        {
            Name = name;
            Ip = ip;
            Port = port;
            Guid = guid;
        }
        public VCPeerInfo()
        {
        }

        [ProtoMember(1)]
        public string Name { get; }
        [ProtoMember(2)]
        public string Ip { get; }
        [ProtoMember(3)]
        public int Port { get; }

        [ProtoMember(4)]
        public Guid Guid { get; }

        public double TcpLatency { get => tcpLatency; set { tcpLatency = value; OnPropertyChanged(); } }
        public double UdpLatency { get => udpLatency; set { udpLatency = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace Videocall.Services.Latency
//{
//    internal class LatencyPublisher
//    {
//       public event EventHandler<LatencyEventArgs> Latency;
//        private MessageHandler MessageHandler;

//        public LatencyPublisher(MessageHandler messageHandler)
//        {
//            MessageHandler = messageHandler;
//            Publish();
//        }

//        private void Publish() 
//        {
//            Task.Run(async () =>
//            {
//                while (true)
//                {

//                    await Task.Delay(900);
//                    Dictionary<Guid, double> sts = MessageHandler.GetUdpPingStatus();
//                    Dictionary<Guid, double> sts2 = MessageHandler.GetTcpPingStatus();
//                    if (sts == null || sts2 == null)
//                        return;
//                    LatencyEventArgs args =  new LatencyEventArgs(sts, sts2);
//                    Latency?.Invoke(this, args);
//                }

//            });
//        }
//    }

//    public class LatencyEventArgs:EventArgs
//    {
//        public Dictionary<Guid, double> UdpLatency;
//        public Dictionary<Guid, double> TcpLatency;

//        public LatencyEventArgs(Dictionary<Guid, double> udpLatency, Dictionary<Guid, double> tcpLatency)
//        {
//            UdpLatency = udpLatency;
//            TcpLatency = tcpLatency;
//        }
//    }
//}

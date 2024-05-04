using NetworkLibrary;
using Protobuff;
using ServiceProvider.Services.Audio;
using ServiceProvider.Services.Audio.Dependency;
using ServiceProvider.Services.ScreenShare;
using ServiceProvider.Services.Video.Camera;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Videocall.Services.File_Transfer;
using Videocall.Services.Latency;
using Videocall.Services.ScreenShare;
using Videocall.Services.Video;

namespace Videocall
{
    public class ServiceHub
    {

        private static ServiceHub instance;
        public static ServiceHub Instance
        {
            get
            {
                if(instance == null)
                    instance = new ServiceHub();
                return instance;
            }
        }
        public AudioHandler AudioHandler { get; private set; }
        public VideoHandler2 VideoHandler { get; private set; }

        public MessageHandler MessageHandler { get; private set; }

        public FileTransferStateManager FileTransfer { get; private set; }

        public LatencyPublisher LatencyPublisher { get; private set; }
        public ScreenShareHandlerH264 ScreenShareHandler { get; private set; }

        public Action<VCStatistics> VideoStatisticsAvailable;
        public Action<int,int> CamSizeFeedbackAvailable;
        public event Action<string, string> LogAvailable;
        private VCStatistics stats;
        private VCStatistics statsPrev;
      
        private ServiceHub()
        {
            
        }
        public void Initialize(IAudioIn audioIn, IAudioOut audioOut,ICameraProvider camProvider,IScreenCapture screenCapture)
        {
            AudioHandler = new AudioHandler(audioIn,audioOut);
            VideoHandler = new VideoHandler2(camProvider);
            ScreenShareHandler = new ScreenShareHandlerH264(screenCapture);

            FileTransfer = new FileTransferStateManager(new FileTransferHelper());
            MessageHandler = new MessageHandler();
            LatencyPublisher = new LatencyPublisher(MessageHandler);

            MessageHandler.OnMessageAvailable += HandleMessage;
            AudioHandler.OnStatisticsAvailable += OnAudioStatsAvailable;
            VideoHandler.CamSizeFeedbackAvailable = (w, h) => CamSizeFeedbackAvailable?.Invoke(w, h);
            PublishStatistics();
        }

        private void PublishStatistics()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(1000);// dont change time
                    var vs = VideoHandler.GetStatistics();
                    var scs = ScreenShareHandler.GetStatistics();

                    stats.OutgoingFrameRate = vs.OutgoingFrameRate + scs.OutgoingFrameRate;
                    stats.IncomingFrameRate = vs.IncomingFrameRate + scs.IncomingFrameRate;
                    stats.TransferRate = scs.TransferRate + vs.TransferRate;
                    stats.AverageLatency = vs.AverageLatency;
                    stats.ReceiveRate = vs.ReceiveRate + scs.ReceiveRate;
                    stats.CurrentMaxBitRate = vs.CurrentMaxBitRate;
                    
                    if(statsPrev != stats)
                    {
                        statsPrev = stats;
                        VideoStatisticsAvailable?.Invoke(stats);
                    }
                }

            });
        }

        private void HandleMessage(MessageEnvelope message)
        {
            if(message.Header == MessageHeaders.MicClosed)
            {
                AudioHandler.FlushBuffers();
            }
            else if (message.Header == MessageHeaders.RemoteClosedCam)
            {
                VideoHandler.FlushBuffers();
            }
        }

        private void OnAudioStatsAvailable(AudioStatistics stats)
        {
            // you can mode it as prop
            VideoHandler.AudioBufferLatency = AudioHandler.BufferedDurationAvg;
        }

       
        public void ResetBuffers()
        {
            AudioHandler.ResetStatistics();
            AudioHandler.FlushBuffers();
            VideoHandler.FlushBuffers();
        }

        public void Log(string logType,string log)
        {
            LogAvailable?.Invoke(logType, log);
        }
    }
}

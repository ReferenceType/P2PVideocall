using NetworkLibrary;
using Protobuff;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Videocall.Services.Latency;
using Videocall.Services.ScreenShare;

namespace Videocall
{
    internal class ServiceHub
    {
        public AudioHandler AudioHandler { get;}
        public VideoHandler VideoHandler { get;}

        public MessageHandler MessageHandler { get;}

        public FileShare FileShare { get;}

        public LatencyPublisher LatencyPublisher { get; }
        public SimpleScreenShareHandler ScreenShareHandler { get; }


        public ServiceHub(AudioHandler audioHandlerAudioHandler,
                          VideoHandler videoHandler,
                          MessageHandler messageHandler,
                          FileShare fileSHare,
                          LatencyPublisher latencyPublisher,
                          SimpleScreenShareHandler screenShareHandler)
        {
            AudioHandler = audioHandlerAudioHandler;
            VideoHandler = videoHandler;
            MessageHandler = messageHandler;
            FileShare = fileSHare;
            LatencyPublisher = latencyPublisher;
            ScreenShareHandler = screenShareHandler;

            messageHandler.OnMessageAvailable += HandleMessage;

            CallStateManager.StaticPropertyChanged += CallStateChanged;
            AudioHandler.OnStatisticsAvailable += OnAudioStatsAvailable;
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
            VideoHandler.AudioBufferLatency = stats.BufferedDuration;
        }

        private void CallStateChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
           if(CallStateManager.GetState() == CallStateManager.CallState.OnCall)
            {
                AudioHandler.ResetStatistics();
                AudioHandler.FlushBuffers();
                VideoHandler.FlushBuffers();
            }
        }
    }
}

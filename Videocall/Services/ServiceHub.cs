using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Videocall.Services.Latency;

namespace Videocall
{
    internal class ServiceHub
    {
        public AudioHandler AudioHandler { get;}
        public VideoHandler VideoHandler { get;}

        public MessageHandler MessageHandler { get;}

        public FileShare FileShare { get;}

        public LatencyPublisher LatencyPublisher { get; }


        public ServiceHub(AudioHandler audioHandlerAudioHandler,
                          VideoHandler videoHandler,
                          MessageHandler messageHandler,
                          FileShare fileSHare,
                          LatencyPublisher latencyPublisher)
        {
            AudioHandler = audioHandlerAudioHandler;
            VideoHandler = videoHandler;
            MessageHandler = messageHandler;
            FileShare = fileSHare;
            LatencyPublisher = latencyPublisher;
        }
    }
}

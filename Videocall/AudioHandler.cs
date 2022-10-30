using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Videocall
{
    internal class AudioHandler
    {

        public Action<byte[], int, int> OnAudioAvailable;
        private WaveOutEvent player;
        private BufferedWaveProvider soundListenBuffer;
        private bool playBackRunning;
        private WaveInEvent waveIn;

        private WaveFormat format = new WaveFormat(48000, 16, 2);

        public AudioHandler()
        {
            soundListenBuffer = new BufferedWaveProvider(format);
            soundListenBuffer.DiscardOnBufferOverflow = true;

            player = new WaveOutEvent();
            player.DesiredLatency = 60;
            player.Init(soundListenBuffer);
            player.Volume = 1;

            waveIn = new WaveInEvent();
            waveIn.WaveFormat = format;
            waveIn.BufferMilliseconds = 60;
            waveIn.RecordingStopped += WaveIn_RecordingStopped;
            
            waveIn.DataAvailable += AudioRecieved;
        }

        private void AudioRecieved(object sender, WaveInEventArgs e)
        {
            try
            {
                OnAudioAvailable?.Invoke(e.Buffer, 0, e.BytesRecorded);

            }
            catch { }
        }

        private void WaveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            playBackRunning = false;
        }

        public void ProcessAudio(byte[] soundBytes)
        {
            soundListenBuffer?.AddSamples(soundBytes, 0, soundBytes.Length);

        }
        public void ProcessAudio(byte[] soundBytes,int offset, int count)
        {
            soundListenBuffer?.AddSamples(soundBytes, offset, count);

        }

        public void StartSpeakers()
        {
            if (player.PlaybackState == PlaybackState.Playing)
                return;
            
            player.Play();
        }

        public void StopSpreakers()
        {
            if (player.PlaybackState == PlaybackState.Playing)

                player.Stop();
        }

        public void StartMic()
        {
            
            waveIn.StartRecording();

        }
    }
}

using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceProvider.Services.Audio.Dependency
{
    public class WaveOutAdapter : IAudioOut
    {
        protected BufferedWaveProvider soundListenBuffer;
        protected VolumeSampleProvider volumeSampleProvider;
        protected IWavePlayer player;
        protected float volume;

        public float Gain { get; set; } = 1;
        public float Volume { get => volume; set { volume = value; volumeSampleProvider.Volume = value; } }
        public TimeSpan BufferDuration => soundListenBuffer?.BufferDuration ?? new TimeSpan(10000);
        public TimeSpan BufferedDuration => soundListenBuffer?.BufferedDuration ?? new TimeSpan(10000);

        public virtual void Init(Waveformat format)
        {
            soundListenBuffer = new BufferedWaveProvider(new WaveFormat(format.Rate, format.Bits, format.Channel));
            soundListenBuffer.BufferLength = 320 * format.AverageBytesPerSecond / 1000;
            soundListenBuffer.DiscardOnBufferOverflow = true;

            volumeSampleProvider = new VolumeSampleProvider(soundListenBuffer.ToSampleProvider());
            volumeSampleProvider.Volume = Gain;

            var player_ = new WaveOutEvent();
            player_.DesiredLatency = 60;
            player_.Init(volumeSampleProvider);
            Interlocked.Exchange(ref player, player_)?.Dispose();
        }
        public void Dispose()
        {
            player?.Dispose();
        }

        public void Stop()
        {
            player?.Stop();
        }

        public void AddSamples(byte[] buffer, int offset_, int pos)
        {
            soundListenBuffer.AddSamples(buffer, offset_, pos);
        }

        public void Play()
        {
            player?.Play();
        }
    }
}

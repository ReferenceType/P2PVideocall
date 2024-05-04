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
    public class WasapiOutAdapter : WaveOutAdapter
    {
        public override void Init(Waveformat format)
        {
            soundListenBuffer = new BufferedWaveProvider(new WaveFormat(format.Rate, format.Bits, format.Channel));
            soundListenBuffer.BufferLength = 320 * format.AverageBytesPerSecond / 1000;
            soundListenBuffer.DiscardOnBufferOverflow = true;

            volumeSampleProvider = new VolumeSampleProvider(soundListenBuffer.ToSampleProvider());
            volumeSampleProvider.Volume = Gain;

            var outputDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
            if (outputDevice == null)
                return;

            var player_ = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 60);
            player_.Init(volumeSampleProvider);
            Interlocked.Exchange(ref player, player_)?.Dispose();
        }

    }
}

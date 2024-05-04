using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceProvider.Services.Audio.Dependency
{
    public class WaveInAdapter : IAudioIn
    {
        public Waveformat Format { get; }
        public event Action<byte[], int, int> SampleAvailable;
        public DeviceInfo SelectedDevice { get; set; } = null;
        public List<DeviceInfo> InputDevices { get; set; } = new List<DeviceInfo>();
        public int CaptureInterval { get; protected set; }

        protected WaveFormat format;
        protected IWaveIn waveIn;

        public WaveInAdapter()
        {

        }

        public virtual List<DeviceInfo> EnumerateDevices()
        {
            return InputDevices;
        }

        public virtual void Init(Waveformat Format, int captureInterval)
        {
            format = new WaveFormat(Format.Rate, Format.Bits, Format.Channel);
            CaptureInterval = captureInterval;


            var waveIn_ = new WaveInEvent();
            waveIn_.BufferMilliseconds = captureInterval;
            waveIn_.WaveFormat = format;
            waveIn_.DataAvailable += MicrophoneSampleAvailable;

            var old = Interlocked.Exchange(ref waveIn, waveIn_);
            if (old != null)
            {
                old.StopRecording();
                old.Dispose();
                waveIn.StartRecording();
            }

        }

        protected void MicrophoneSampleAvailable(object? sender, WaveInEventArgs e)
        {
            SampleAvailable?.Invoke(e.Buffer, 0, e.BytesRecorded);
        }

        public void Dispose()
        {
            if (waveIn != null)
            {
                waveIn.DataAvailable -= MicrophoneSampleAvailable;
                waveIn.Dispose();
            }
            SampleAvailable = null;


        }

        public void StartRecording()
        {
            waveIn?.StartRecording();
        }

        public void StopRecording()
        {
            waveIn?.StopRecording();
        }
    }
}

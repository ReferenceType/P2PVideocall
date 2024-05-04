using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Videocall;

namespace ServiceProvider.Services.Audio.Dependency
{
    public class Waveformat
    {
        public int Rate, Bits, Channel;
        public readonly int AverageBytesPerSecond;

        public Waveformat(int rate, int bits, int channel)
        {
            Rate = rate;
            Bits = bits;
            Channel = channel;
            var blockAlign = (short)(Channel * (bits / 8));
            AverageBytesPerSecond = Rate * blockAlign;
        }
    }
    public class DeviceInfo
    {
        public string Name { get; set; }
    }
    public class WasapiInAdapter : WaveInAdapter
    {

        public override List<DeviceInfo> EnumerateDevices()
        {
            InputDevices.Clear();
            var enumerator = new MMDeviceEnumerator();
            foreach (MMDevice wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                InputDevices.Add(new DeviceInfo() { Name = wasapi.FriendlyName });
            }
            var inp = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            enumerator.Dispose();

            if (inp == null)
                return InputDevices;

            SelectedDevice = InputDevices.Where(x => x.Name == inp.FriendlyName).FirstOrDefault()!;
            return InputDevices;
        }

        public override void Init(Waveformat Format, int captureInterval)
        {
            format = new WaveFormat(Format.Rate, Format.Bits, Format.Channel);
            CaptureInterval = captureInterval;


            MMDevice inputDevice = null;

            var enumerator = new MMDeviceEnumerator();
            List<MMDevice> devices = new List<MMDevice>();
            foreach (MMDevice wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                devices.Add(wasapi);
            }

            if (SelectedDevice != null)
            {
                inputDevice = devices.Where(x => x.FriendlyName == SelectedDevice.Name).FirstOrDefault()!;

            }

            if (inputDevice == null)
            {
                inputDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }

            if (inputDevice == null)
            {
                return;
            }


            var waveIn_ = new WasapiCapture(inputDevice, true, CaptureInterval);
            waveIn_.WaveFormat = format;
            waveIn_.DataAvailable += MicrophoneSampleAvailable;
            var old = Interlocked.Exchange(ref waveIn, waveIn_);

        }


    }
}

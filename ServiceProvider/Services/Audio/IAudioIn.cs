
using ServiceProvider.Services.Audio.Dependency;

namespace ServiceProvider.Services.Audio
{
    public interface IAudioIn
    {
        int CaptureInterval { get; }
      
        Waveformat Format { get; }
        List<DeviceInfo> InputDevices { get; set; }
        DeviceInfo SelectedDevice { get; set; }

        event Action<byte[], int, int> SampleAvailable;

        void Dispose();
        List<DeviceInfo> EnumerateDevices();
        void Init(Waveformat Format, int captureInterval);
        void StartRecording();
        void StopRecording();
    }
}
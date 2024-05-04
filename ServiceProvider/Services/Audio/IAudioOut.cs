
using ServiceProvider.Services.Audio.Dependency;

namespace ServiceProvider.Services.Audio
{
    public interface IAudioOut
    {
        TimeSpan BufferDuration { get; }
        TimeSpan BufferedDuration { get; }
        float Gain { get; set; }
        float Volume { get; set; }

        void AddSamples(byte[] buffer, int offset_, int pos);
        void Dispose();
        void Init(Waveformat format);
        void Play();
        void Stop();
    }
}

namespace Videocall.Services.ScreenShare
{
    public interface IScreenCapture
    {
        void CaptureAuto(int targetFrameRate, Action<EncodedBmp> onCaptured);
        void Dispose();
        void Init(double scale = 0.5, int screen = 0, int device = 0);
        void StopCapture();
    }
}
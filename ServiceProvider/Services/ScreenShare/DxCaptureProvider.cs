using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Videocall.Services.ScreenShare;

namespace ServiceProvider.Services.ScreenShare
{
    public class DxCaptureProvider : IScreenCapture
    {
        DXScreenCapture screenCapture;
        public void Init(double scale = 0.5, int screen = 0, int device = 0)
        {
            screenCapture = new DXScreenCapture();
            screenCapture.Init(scale, screen, device);
        }
        public void CaptureAuto(int targetFrameRate, Action<EncodedBmp> onCaptured)
        {
            screenCapture.CaptureAuto(targetFrameRate, onCaptured);
        }

        public void Dispose()
        {
           screenCapture?.Dispose();
        }

       

        public void StopCapture()
        {
            screenCapture?.StopCapture();
        }
    }
}

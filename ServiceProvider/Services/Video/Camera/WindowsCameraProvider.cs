using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceProvider.Services.Video.Camera
{
    public class WindowsCameraProvider : ICameraProvider
    {
        readonly Mat frame = new Mat();
        readonly VideoCapture capture;
        private int frameWidth => capture.FrameWidth;
        private int frameHeight => capture.FrameHeight;

        public WindowsCameraProvider(int camIdx)
        {
            capture = new VideoCapture(camIdx, VideoCaptureAPIs.MSMF);
        }

        public int FrameWidth { get => frameWidth; set { capture.FrameWidth = value; } }
        public int FrameHeight { get => frameHeight; set => capture.FrameHeight = value; }


        public void Open(int camIdx)
        {
            capture.Open(camIdx);
        }

        public bool IsOpened()
        {
            return capture.IsOpened();
        }

        public void Release()
        {
            capture?.Release();
        }

        public void Dispose()
        {
            capture.Dispose();
        }

        public bool Grab()
        {
            return capture.Grab();
        }

        public bool Retrieve(ref ImageReference im)
        {

            var res = capture.Retrieve(frame);
            if (res == false)
            {
                return false;
            }
            if (im == null)
            {
                im = ImageReference.FromMat(frame);
            }
            else
            {
                im.Update(frame);

            }
            return res;

        }
    }
}

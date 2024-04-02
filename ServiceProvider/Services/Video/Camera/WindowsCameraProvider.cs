using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceProvider.Services.Video.Camera
{
    public class WindowsCameraProvider : ICameraProvider
    {
        private ConcurrentBag<Mat> mats = new ConcurrentBag<Mat>();
        private ConcurrentBag<ImageReference> imrefs = new ConcurrentBag<ImageReference>();
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

        public bool Retrieve(out ImageReference im)
        {
            mats.TryTake(out var frame);
            imrefs.TryTake(out im);

            if (frame == null)
            {
                frame= new Mat();
            }
            if (frame.Width != capture.FrameWidth || frame.Height != capture.FrameHeight)
            {
                frame.Dispose();
                frame =  new Mat();
            }

            var res = capture.Retrieve(frame);
            if (res == false)
            {
                return false;
            }
            if (im == null)
            {
                im = ImageReference.FromMat(frame,ReturnMat);
            }
            else
            {
                im.Update(frame);

            }
            return res;

        }

        private void ReturnMat(ImageReference reference)
        {
            imrefs.Add(reference);
            mats.Add((Mat)reference.underlyingData);
        }
    }
}

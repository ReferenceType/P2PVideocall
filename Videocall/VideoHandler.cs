using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Videocall
{
    internal class VideoHandler
    {
        private VideoCapture capture;
        public Action<byte[],Mat> OnImageAvailable;
        public int captureRateMs = 50;
        public int compressionLevel = 30;
        private bool captureRunning = false;

        public bool ObtainCamera()
        {
            capture = new VideoCapture(0);
            capture.Open(0);

            return capture.IsOpened();
        }

        public void CloseCamera()
        {
            if (capture != null && capture.IsOpened())
            {
                capture.Release();
            }
        }
        public void StartCapturing()
        {
            if(captureRunning)
                return;
            captureRunning = true;
            var frame = new Mat();
            if (!capture.IsOpened())
            {
               if(!ObtainCamera())
                    return;

            }
            //capture.FrameHeight = 100;
            //capture.FrameWidth = 100;
            //capture.FourCC = "H264";
            Thread t = new Thread(() =>
            {
                try
                {
                    while (capture.IsOpened())
                    {
                        capture.Read(frame);
                        //ImwriteFlags
                        var param = new int[7];
                        param[0] = 1;
                        param[1] = compressionLevel;
                        param[2] = 2;
                        param[3] = 0;
                        param[4] = 3;

                        byte[] imageBytes;
                        try
                        {
                            imageBytes = frame.ImEncode(ext: ".jpg", param);
                            OnImageAvailable?.Invoke(imageBytes,frame);
                        }
                        catch { break; }

                        Thread.Sleep(captureRateMs);
                    }
                }
                finally
                {
                    captureRunning = false;
                    CloseCamera();
                }
                
            });
            t.Start();
           

        }
    }
}

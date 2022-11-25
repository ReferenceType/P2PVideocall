using OpenCvSharp;
using OpenCvSharp.Extensions;
using ProtoBuf;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;


namespace Videocall
{
    [ProtoContract]
    class ImageMessage:IProtoMessage
    {
        [ProtoMember(1)]
        public DateTime TimeStamp;

        [ProtoMember(2)]
        public byte[] Frame;
    }
    internal class VideoHandler
    {
        private VideoCapture capture;
        public Action<byte[],Mat> OnCameraImageAvailable;
        public Action<Mat> OnNetworkFrameAvailable;

        public int captureRateMs = 50;
        public int compressionLevel = 30;

        private bool captureRunning = false;
        private ConcurrentDictionary<DateTime,Mat> frameQueue = new ConcurrentDictionary<DateTime, Mat>();
        private AutoResetEvent imgReady = new AutoResetEvent(false);
        private DateTime lastProcessedTimestamp = DateTime.Now;

        public VideoHandler()
        {
            Thread t = new Thread(() =>
            {
                while (true)
                {
                    imgReady.WaitOne();
                    if(frameQueue.Count > 3)
                    {
                        var samplesOrdered = frameQueue.OrderByDescending(x => x.Key);
                        var lastFrame= samplesOrdered.Last();

                        if (lastProcessedTimestamp < lastFrame.Key)
                        {
                            OnNetworkFrameAvailable?.Invoke(lastFrame.Value);
                            lastProcessedTimestamp= lastFrame.Key;
                        }

                        frameQueue.TryRemove(lastFrame.Key, out _); 
                        
                    }
                }

            });
            t.Priority = ThreadPriority.AboveNormal;
            t.Start();
        }

        public bool ObtainCamera()
        {
            if (capture != null && capture.IsOpened()) return true;

            capture = new VideoCapture(CaptureDevice.FFMPEG);
            capture.Open(0);
            return capture.IsOpened();
        }

        public void CloseCamera()
        {
            if (capture != null && capture.IsOpened())
            {
                capture.Release();
                capture = null;
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


                        ImageEncodingParam[] par= new ImageEncodingParam[1];
                        //par[0] = new ImageEncodingParam(ImwriteFlags.JpegProgressive, 1);
                        //par[1] = new ImageEncodingParam(ImwriteFlags.JpegQuality, compressionLevel);
                        par[0] = new ImageEncodingParam(ImwriteFlags.WebPQuality, compressionLevel);
                        //par[1] = new ImageEncodingParam(ImwriteFlags.JpegQuality, compressionLevel);

                        byte[] imageBytes;
                        try
                        {
                            imageBytes = frame.ImEncode(ext: ".webp", par);
                            int a = imageBytes.Length;
                            Console.WriteLine(a);
                            //imageBytes = frame.ImEncode(ext: ".jpg", param);
                            //int b = imageBytes.Length;
                            //Console.WriteLine(b);


                            OnCameraImageAvailable?.Invoke(imageBytes,frame);
                        }
                        catch { break; }

                        Thread.Sleep(Math.Max(0,captureRateMs-16));
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

        internal void HandleIncomingImage(ImageMessage payload)
        {
            Mat img = Cv2.ImDecode(payload.Frame, ImreadModes.Unchanged);
            frameQueue[payload.TimeStamp] = img;
            imgReady.Set();
        }
    }
}

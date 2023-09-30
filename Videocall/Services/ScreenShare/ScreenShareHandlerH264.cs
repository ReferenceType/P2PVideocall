using OpenCvSharp;
using System;
using Videocall.Services.Video.H264;
using System.Threading;
using System.Collections.Concurrent;
using Videocall.Settings;
using static H264Sharp.Encoder;
using NetworkLibrary.Components;


namespace Videocall.Services.ScreenShare
{
    public enum Resolution
    {
        _2180p = 9_416_000,
        _2080p = 8_294_400,
        _1440p = 3_686_400,
        _1180p = 2_304_000,
        _1080p = 2_073_600,
        _960p = 1_660_000,
        _840p = 1_360_000,
        _720p = 1_094_400,
        _480p = 500000
    }

    internal class ScreenShareHandlerH264
    {

        public Action<Mat> LocalImageAvailable;
        public Action<byte[], int, bool> OnBytesAvailable;
        public Action<Mat> RemoteImageAvailable;
        public Action KeyFrameRequested;
        public Action<Action<PooledMemoryStream>, int, bool> DecodedFrameWithAction;
        private Resolution resolution = Resolution._1180p;

        private ConcurrentQueue<Mat> YuvQueue = new ConcurrentQueue<Mat>();
        private ConcurrentStack<Mat> YuvPool = new ConcurrentStack<Mat>();
        private ManualResetEvent encoderRunning = new ManualResetEvent(true);
        private AutoResetEvent startEncoding = new AutoResetEvent(false);
        private DXScreenCapture screenCapture = new DXScreenCapture();
        private H264Transcoder transcoder;
        private Thread encodethread = null;

        private int running = 0;
        private int fps = 17;
        private int targetFps = 15;
        private int capturedFrames = 0;
        private int incomingFrames = 0;
        private int bytesSent = 0;
        private int bytesReceived = 0;
        private bool parallel = false;
        private bool captureActive = false;

     
        public ScreenShareHandlerH264()
        {
            SetupTranscoder();
        }
       
        void SetupTranscoder()
        {
            transcoder = new H264Transcoder(30, 3_000_000);
            transcoder.SetupTranscoder(0, 0, ConfigType.ScreenCaptureBasic);
            transcoder.DecodedFrameAvailable = (f) => { incomingFrames++; RemoteImageAvailable?.Invoke(f); };
            //transcoder.EncodedFrameAvailable = (b, o, k) => { bytesSent += o; OnBytesAvailable?.Invoke(b, o, k); };

            transcoder.EncodedFrameAvailable2 = (action, size, isKeyFrame) => 
            { 
                bytesSent += size;
                DecodedFrameWithAction.Invoke(action, size,isKeyFrame);
            };
            transcoder.KeyFrameRequested = () => KeyFrameRequested?.Invoke();
            transcoder.SetKeyFrameInterval(-1);
        }
        public void StartCapture()
        {
            try
            {
                if (captureActive) return;
                captureActive = true;
                var res = ScreenInformations.GetDisplayResolution();

                double screenWidth = res.Width;//3840; //SystemParameters.FullPrimaryScreenWidth;
                double screenHeight = res.Height;//2400; // SystemParameters.FullPrimaryScreenHeight;

                double scale = 1;
                int maxPixelsize = (int)resolution;//2304000
                if (screenWidth * screenHeight > maxPixelsize)
                {
                    CalculateResolutionPreserveRatio(screenWidth, screenHeight, maxPixelsize, out var frameHeight, out var frameWidth);
                    scale = frameWidth / screenWidth;
                }
                if (screenCapture == null)
                    screenCapture = new DXScreenCapture();
                screenCapture.Init(scale, SettingsViewModel.Instance.Config.ScreenId, SettingsViewModel.Instance.Config.GpuId);
                keyFrameRequested = true;

                BeginCaptureThread((float)scale);
            }
            catch (Exception ex)
            {
                DebugLogWindow.AppendLog("Error:", "ScreenshareHandler encountered an error:" + ex.Message);
            }

        }

        private void BeginCaptureThread(float scale)
        {
            Mat mainMat = null;
            Mat Yuv420Mat = new Mat();
            Mat smallMat = new Mat();
            screenCapture.CaptureAuto(targetFps,
               (img) =>
               {
                   capturedFrames++;
                   parallel = SettingsViewModel.Instance.Config.MultiThreadedScreenShare;
                   if (transcoder.encoderWidth != img.Width || transcoder.encoderHeight != img.Height)
                   {
                       transcoder.ApplyChanges(fps, SettingsViewModel.Instance.Config.SCTargetBps * 1000,
                           img.Width, img.Height, ConfigType.ScreenCaptureBasic);
                   }

                  
                   if (!YuvPool.TryPop(out Yuv420Mat))
                       Yuv420Mat = new Mat();

                   unsafe
                   {
                       fixed (byte* ptr = &img.stream.GetBuffer()[img.startInx])
                       {
                           mainMat = new Mat(img.Height, img.Width, MatType.CV_8UC3, (IntPtr)ptr, img.Stride);
                       }

                   }
                   var src = InputArray.Create(mainMat);
                   var YuvDst = OutputArray.Create(Yuv420Mat);
                   Cv2.CvtColor(src, YuvDst, ColorConversionCodes.BGR2YUV_I420);


                   if (!parallel)
                   {

                       Encode(Yuv420Mat);
                   }
                   else
                   {
                       encoderRunning.WaitOne();
                       YuvQueue.Enqueue(Yuv420Mat);
                       EncodeParallel();
                   }


                   var dst = OutputArray.Create(smallMat);
                   Cv2.Resize(src, dst, new OpenCvSharp.Size(320, 240), interpolation: InterpolationFlags.Nearest);
                   LocalImageAvailable?.Invoke(smallMat);

                   GC.KeepAlive(mainMat);
                   GC.KeepAlive(img);
                   GC.KeepAlive(Yuv420Mat);
               });
        }

       
      
        private void EncodeParallel()
        {
            // thread already running
            if (Interlocked.CompareExchange(ref running, 1, 0) == 1)
            {
                startEncoding.Set();
            }
            else
            {
                encodethread = new Thread(() =>
                {
                    try
                    {
                        while (true)
                        {
                            startEncoding.WaitOne();
                            encoderRunning.Reset();
                            while (YuvQueue.TryDequeue(out var mat))
                            {
                                Encode(mat);
                            }
                            encoderRunning.Set();
                        }
                    }
                    finally 
                    {
                        running = 0;
                        encoderRunning.Set();
                    }
                

                });
                startEncoding.Set();
                encodethread.Start();
            }

        }


        
        private void Encode(Mat m)
        {
            if (keyFrameRequested)
            {
                keyFrameRequested = false;
                transcoder?.ForceIntraFrame();
            }
            unsafe
            {
                transcoder?.Encode(m.DataPointer);
            }
            YuvPool.Push(m);
        }
      
        void CalculateResolutionPreserveRatio(double originalWidth, double originalHeight, int targetArea, out int newHeight, out int newWidth)
        {
            double newW = Math.Sqrt(((originalWidth / originalHeight) * targetArea));
            double newH = targetArea / newW;

            newWidth = (int)Math.Round(newW);
            newHeight = (int)Math.Round(newH - (newWidth - newW));

            if (newWidth % 2 == 1)
                newWidth++;
            if (newHeight % 2 == 1)
                newHeight++;

        }

        private object locker = new object();
        public unsafe void HandleNetworkImageBytes(DateTime timeStamp, byte[] payload, int payloadOffset, int payloadCount)
        {
            bytesReceived += payloadCount;
            lock (locker)
            {
                transcoder.HandleIncomingFrame(timeStamp, payload, payloadOffset, payloadCount);
            }
        }

        internal void StopCapture()
        {
            try
            {
                captureActive = false;
                screenCapture?.StopCapture();
                screenCapture?.Dispose();
                screenCapture = null;
                LocalImageAvailable?.Invoke(null);

            }
            catch (Exception e) { }

        }
        bool keyFrameRequested = false;
        int count = 0;



        internal void ForceKeyFrame()
        {
            keyFrameRequested = true;
        }

        internal void ApplyChanges(string resolution, int sCTargetFps)
        {
            if (!string.IsNullOrEmpty(resolution))
                this.resolution = (Resolution)Enum.Parse(typeof(Resolution), resolution);
            targetFps = sCTargetFps;
            if (captureActive)
            {
                StopCapture();
                Thread.Sleep(1);
                StartCapture();
            }
        }

        public VCStatistics GetStatistics()
        {
            var st = new VCStatistics
            {
                OutgoingFrameRate = capturedFrames,
                IncomingFrameRate = incomingFrames,
                TransferRate = (float)bytesSent / 1000,
                ReceiveRate = (float)bytesReceived / 1000,
            };

            capturedFrames = 0;
            incomingFrames = 0;
            bytesSent = 0;
            bytesReceived = 0;

            return st;
        }
    }
}

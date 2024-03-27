using OpenCvSharp;
using System;
using Videocall.Services.Video;
using System.Threading;
using System.Collections.Concurrent;
using NetworkLibrary.Components;
using H264Sharp;
using ServiceProvider.Services.Video;
using Videocall.Services.Video.H264;
using System.Diagnostics;


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

    public class ScreenShareHandlerH264
    {

        public Action<ImageReference> LocalImageAvailable;
        public Action<byte[], int, bool> OnBytesAvailable;
        public Action<ImageReference> RemoteImageAvailable;
        public Action KeyFrameRequested;
        public Action<byte[], int, int> MarkingFeedback;
        public Action<byte[], int, int> LtrRecoveryRequest;
        public Action<Action<PooledMemoryStream>, int, bool> DecodedFrameWithAction;
        public int ScreenId;
        public int GpuId;
        public int TargetKBps = 1000;
        public bool EnableParalelisation;
        private Resolution resolution = Resolution._1180p;

        private ConcurrentQueue<EncodedBmp> EncodedBitmapQueue = new ConcurrentQueue<EncodedBmp>();
        private ConcurrentStack<ImageData> ImageDataPool = new ConcurrentStack<ImageData>();

        private ManualResetEvent encoderRunning = new ManualResetEvent(true);
        private AutoResetEvent startEncoding = new AutoResetEvent(false);
        private DXScreenCapture screenCapture = new DXScreenCapture();
        private H264Transcoder transcoder;
        private Thread encodethread = null;
        private ConfigType configType = ConfigType.CameraCaptureAdvanced;
        private int running = 0;
        private int targetFps = 15;
        private int capturedFrames = 0;
        private int incomingFrames = 0;
        private int bytesSent = 0;
        private int bytesReceived = 0;
        private bool parallel = false;
        private bool captureActive = false;
        private bool pendingChanges = false;
     
        public ScreenShareHandlerH264()
        {
            SetupTranscoder();
        }
       
        void SetupTranscoder()
        {
            transcoder = new H264Transcoder(targetFps, 2_000_000);
            transcoder.SetupTranscoder(0, 0, configType);
            transcoder.DecodedFrameAvailable = (f) => { incomingFrames++; RemoteImageAvailable?.Invoke(ImageReference.FromRgbImage(f)); };
            transcoder.EncodedFrameAvailable = (b, o, k) => { bytesSent += o; OnBytesAvailable?.Invoke(b, o, k); };

            //transcoder.EncodedFrameAvailable2 = (action, size, isKeyFrame) => 
            //{ 
            //    bytesSent += size;
            //    DecodedFrameWithAction.Invoke(action, size,isKeyFrame);
            //};

            transcoder.KeyFrameRequested = () => KeyFrameRequested?.Invoke();
            transcoder.MarkingFeedback = (b,o,c) => MarkingFeedback?.Invoke(b,o,c);
            transcoder.LtrRecoveryRequest = (b,o,c) => LtrRecoveryRequest?.Invoke(b,o,c);
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
                screenCapture.Init(scale, ScreenId, GpuId);
                keyFrameRequested = true;

                BeginCaptureThread((float)scale);
            }
            catch (Exception ex)
            {
                ServiceHub.Instance.Log("Error:", "ScreenshareHandler encountered an error:" + ex.Message);
            }

        }

        private void BeginCaptureThread(float scale)
        {
            Mat mainMat = null;
            Mat Yuv420Mat = new Mat();
            Mat smallMat = new Mat();
            RgbImage smallThumb =null;
            screenCapture.CaptureAuto(targetFps,
               (img) =>
               {
                   capturedFrames++;
                   parallel = EnableParalelisation;
                   if (pendingChanges || transcoder.encoderWidth != img.Width || transcoder.encoderHeight != img.Height)
                   {
                       pendingChanges = false;
                       transcoder.ApplyChanges(targetFps,TargetKBps * 1000, img.Width, img.Height, configType);
                   }

                 

                   

                   if (!parallel)
                   {
                       ImageData data_ = new ImageData(ImageType.Bgr,
                                                      img.Width,
                                                      img.Height,
                                                      img.Stride,
                                                      img.stream.GetBuffer(),
                                                      img.startInx,
                                                      img.Width * img.Height * 3);

                       Encode(data_);
                       screenCapture.ReturnImage(img);

                   }
                   else
                   {
                       encoderRunning.WaitOne();
                       EncodedBitmapQueue.Enqueue(img);
                       EncodeParallel();
                   }

                   //unsafe
                   //{
                   //    fixed (byte* ptr = &img.stream.GetBuffer()[img.startInx])
                   //    {
                   //        mainMat = new Mat(img.Height, img.Width, MatType.CV_8UC3, (IntPtr)ptr, img.Stride);
                   //    }

                   //}
                   //var src = InputArray.Create(mainMat);
                   //var dst = OutputArray.Create(smallMat);
                   //Cv2.Resize(src, dst, new OpenCvSharp.Size(mainMat.Width / 4, mainMat.Height / 4), interpolation: InterpolationFlags.Nearest);
                   //LocalImageAvailable?.Invoke(ImageReference.FromMat(smallMat));

                   Stopwatch sww = Stopwatch.StartNew();

                   ImageData data = new ImageData(ImageType.Bgr,
                                                     img.Width,
                                                     img.Height,
                                                     img.Stride,
                                                     img.stream.GetBuffer(),
                                                     img.startInx,
                                                     img.Width * img.Height * 3);

                   int mul = 8;
                   if(smallThumb == null)
                   {
                       smallThumb = new RgbImage(data.Width / mul, data.Height / mul);
                   }
                   if (smallThumb.Width != data.Width / mul || smallThumb.Height != data.Height / mul)
                   {
                       smallThumb.Dispose();
                       smallThumb = new RgbImage(data.Width / mul, data.Height / mul);

                   }

                   transcoder.Downscale(data, smallThumb, mul);
                   sww.Stop();
                   Trace.WriteLine("DS " + sww.ElapsedTicks);
                   LocalImageAvailable?.Invoke(ImageReference.FromRgbImage(smallThumb));
                  
                   //smallThumb.Dispose();
                   //smallThumb = null;



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
                            while (EncodedBitmapQueue.TryDequeue(out var img))
                            {
                                ImageData data = new ImageData(ImageType.Bgr, img.Width, img.Height, img.Stride, img.stream.GetBuffer(), img.startInx, img.Width * img.Height * 3);

                                Encode(data);
                                screenCapture.ReturnImage(img);

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


        int cc = 0;
        private void Encode(ImageData m)
        {
            //if (cc++ > 5)
            //{
            //    cc = 0;
            //    return;
            //}
            if (keyFrameRequested)
            {
                keyFrameRequested = false;
                transcoder?.ForceIntraFrame();
            }
            unsafe
            {
                transcoder?.Encode(m);
            }

           // YuvPool.Push(m);
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

        public void StopCapture()
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



        public void ForceKeyFrame()
        {
            keyFrameRequested = true;
        }

        public void ApplyChanges(string resolution, int sCTargetFps,int targetBps, string config)
        {
            if (!string.IsNullOrEmpty(resolution))
                this.resolution = (Resolution)Enum.Parse(typeof(Resolution), resolution);

            targetFps = sCTargetFps;
            TargetKBps=targetBps;

            if(config=="CameraCapture")
                configType = ConfigType.CameraCaptureAdvanced;
            else
                configType = ConfigType.ScreenCaptureAdvanced;

            if (captureActive)
            {
                StopCapture();
                Thread.Sleep(1);
                pendingChanges = true;
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

        public void HandleMarkingFeedback(byte[] payload, int payloadOffset, int payloadCount)
        {
           transcoder.SetMarkingFeedback(payload, payloadOffset, payloadCount);
        }

        public void HandleLtrRecovery(byte[] payload, int payloadOffset, int payloadCount)
        {
            transcoder.SetLTRRecoverRequest(payload, payloadOffset, payloadCount);
        }

        public void ReturnImage(ImageReference image)
        {
            if(image.underlyingData is RgbImage)
                transcoder.ReturnImage((RgbImage)image.underlyingData);
        }
    }
}

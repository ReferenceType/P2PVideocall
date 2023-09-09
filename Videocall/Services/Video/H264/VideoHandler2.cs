using NetworkLibrary;
using NetworkLibrary.Components;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Videocall.Settings;
using static H264Sharp.Encoder;

namespace Videocall.Services.Video.H264
{
    
    internal class VideoHandler2
    {
        public Action<byte[], int> OnBytesAvailable;
        public Action<Mat> OnLocalImageAvailable;
        public Action<Mat> OnRemoteImageAvailable;
        public Action<Action<PooledMemoryStream>, int, bool> OnBytesAvailableAction;
        public Action KeyFrameRequested;

        public int CaptureIntervalMs = 43;
        public int TargetBitrate = 1_500_000;

        public int VideoLatency { get; internal set; } = 200;
        public int AudioBufferLatency { get; internal set; } = 0;
        public double AverageLatency { get; private set; } = -1;

        public ConcurrentBag<Mat> MatPool = new ConcurrentBag<Mat>();

        private object frameQLocker = new object();
        private VideoCapture capture;
        private H264Transcoder transcoder;
        private readonly ConcurrentDictionary<DateTime, Mat> frameQueue = new ConcurrentDictionary<DateTime, Mat>();
        private readonly ConcurrentDictionary<Guid, DateTime> timeDict = new ConcurrentDictionary<Guid, DateTime>();
        private readonly ManualResetEvent consumerSignal = new ManualResetEvent(false);
        private readonly ManualResetEvent obtainingCamera = new ManualResetEvent(false);
        private Thread captureThread = null;
        private DateTime lastProcessedTimestamp = DateTime.Now;
        private DateTime latestAck;
        private ConfigType configType = ConfigType.CameraBasic;
        private int incomingFrameCount;
        private int capturedFrameCnt = 0;
        private int incomingFrameRate = 0;
        private int outgoingFrameRate = 0;
        private int bytesSent = 0;
        private int bytesReceived = 0;
        private int camIdx;
        private int fps = 30;
        private int unAcked = 0;
        private int actualFps = 0;
        private int currentBps = 0;
        private int frameQueueCount = 0;
        private int frameWidth = 640;
        private int frameHeight = 480;
        private int minBps => (TargetBitrate * 10) / 100;
        private int periodicKeyFrameInterval = -1;
        private long avgDivider = 2;
        private bool paused = false;
        private bool captureRunning = false;
        private bool keyFrameRequested;
        private bool adjustCamSize = false;
        private bool enableCongestionAvoidance => SettingsViewModel.Instance.Config.EnableCongestionAvoidance;
        public VideoHandler2()
        {
            SetupTranscoder(fps, TargetBitrate);
           
            // audio jitter synchronization
            Thread t = new Thread(() =>
            {
                while (true)
                {
                    if(Interlocked.CompareExchange(ref frameQueueCount,0,0) == 0)
                        consumerSignal.WaitOne();

                    Thread.Sleep(1);//slow dispatch
                    var videoJitterLatency = transcoder.Duration;

                    if (frameQueue.Count > (AudioBufferLatency-Math.Min(AudioBufferLatency, videoJitterLatency)) / (1000 / Math.Max(1,incomingFrameRate)))
                    {
                        KeyValuePair<DateTime, Mat> lastFrame;// oldest
                        lock (frameQLocker)
                        {
                            var samplesOrdered = frameQueue.OrderByDescending(x => x.Key);
                            lastFrame = samplesOrdered.Last();
                        }

                        if (lastProcessedTimestamp < lastFrame.Key)
                        {
                            OnRemoteImageAvailable?.Invoke(lastFrame.Value);
                            lastProcessedTimestamp = lastFrame.Key;
                        }

                        if(frameQueue.TryRemove(lastFrame.Key, out _))
                            Interlocked.Decrement(ref frameQueueCount);
                      
                    }
                }

            });
            t.Start();
        }
       
        private void SetupTranscoder(int fps, int bps)
        {
            transcoder = new H264Transcoder(MatPool, fps, bps);
            //transcoder.EncodedFrameAvailable = HandleEncodedFrame;
            transcoder.EncodedFrameAvailable2 = HandleEncodedFrame2;
            transcoder.DecodedFrameAvailable = HandleDecodedFrame;
            transcoder.KeyFrameRequested = () => KeyFrameRequested?.Invoke() ;
            transcoder.SetKeyFrameInterval(periodicKeyFrameInterval);
        }

       
        public bool ObtainCamera()
        {
            if (capture != null && capture.IsOpened()) return true;
            obtainingCamera.Reset();

            if(captureThread!=null && captureThread.IsAlive)
            {
                captureThread.Join();
            }
            capture = new VideoCapture(camIdx, VideoCaptureAPIs.WINRT);
           
            capture.Open(camIdx);
            capture.FrameWidth = frameWidth;
            capture.FrameHeight = frameHeight;
            obtainingCamera.Set();
            DebugLogWindow.AppendLog("[Info] Camera Backend: ", capture.GetBackendName());

            transcoder.SetupTranscoder(capture.FrameWidth,capture.FrameHeight,configType);

            return capture.IsOpened();
        }

        public void CloseCamera()
        {
            if (capture != null)
            {
                try
                {
                    capture.Release();
                } 
                catch { DebugLogWindow.AppendLog("Error", "Capture Release Failed"); }

                capture = null;
                Interlocked.Exchange(ref frameQueueCount,0);

                while (MatPool.TryTake(out var mat))
                    mat.Dispose();

                OnLocalImageAvailable?.Invoke(null);
            }
        }
       
        public void StartCapturing()
        {
            adjustCamSize = false;
            obtainingCamera.WaitOne();
            if (captureRunning)
                return;

            captureRunning = true;
            var frame = new Mat();
            var f = new Mat();
            if (!capture.IsOpened())
            {
                if (!ObtainCamera())
                {
                    captureRunning = false;
                    return;
                }
            }

           Stopwatch sw = new Stopwatch();
            captureThread = new Thread(() =>
            {
                try
                {
                    sw.Start();
                    int remainderTime = 0;
                    while (capture != null && capture.IsOpened())
                    {
                        if (capture == null || !capture.IsOpened())
                            return;
                        if (paused)
                        {
                            Thread.Sleep(100);
                            continue;
                        }

                        if (adjustCamSize)
                        {
                            adjustCamSize = false;
                            if(capture.FrameWidth != frameWidth || capture.FrameHeight != frameHeight)
                            {
                                capture.Release();
                                capture.Dispose();
                                capture = new VideoCapture(camIdx,VideoCaptureAPIs.MSMF);
                                capture.Open(camIdx);
                                capture.FrameWidth = frameWidth;
                                capture.FrameHeight = frameHeight;
                                frameWidth = capture.FrameWidth;
                                frameHeight = capture.FrameHeight;
                            }
                           
                            transcoder.ApplyChanges(fps,TargetBitrate,frameWidth,frameHeight,configType);
                            frame =  new Mat();
                            keyFrameRequested = true;
                        }
                        Thread.Sleep(1);
                        if (!capture.Grab())
                        {
                            return;
                        }
                      

                        int sleepTime = CaptureIntervalMs - ((int)sw.ElapsedMilliseconds + remainderTime) ;
                        if (sleepTime > 0)
                        {
                            continue;
                        }
                       
                        capture.Retrieve(frame);
                         if (frame.Width == 0 || frame.Height == 0)
                            return;

                        remainderTime = -sleepTime;// to positive
                        sw.Restart();

                        try
                        {
                            EncodeFrame(frame);
                            OnLocalImageAvailable?.Invoke(frame);
                            capturedFrameCnt++;
                         
                        }
                        catch (Exception ex) { DebugLogWindow.AppendLog(" Capture encoding failed: ", ex.Message); };
                    }
                }
                finally
                {
                    captureRunning = false;
                    CloseCamera();
                    OnLocalImageAvailable?.Invoke(null);
                }

            });
            captureThread.Start();
            GC.KeepAlive(frame);

        }
        Mat m = null;
        private void EncodeFrame(Mat frame)
        {
            try
            {
                if (m == null)
                    m = new Mat();

                var src = InputArray.Create(frame);
                var @out = OutputArray.Create(m);
                Cv2.CvtColor(src, @out, ColorConversionCodes.BGR2YUV_I420);

                if (keyFrameRequested)
                {
                    keyFrameRequested = false;
                    transcoder.ForceIntraFrame();
                    Console.WriteLine("Forcing Key Frame");                    
                }
                else
                {
                    unsafe
                    {
                        transcoder.Encode(m.DataPointer);
                    }

                }

            }
            catch (Exception ex) { DebugLogWindow.AppendLog(" Capture encoding failed: ", ex.Message); };
        }

        private void HandleDecodedFrame(Mat mat)
        {
            lock (frameQLocker)
            {
                if (frameQueue.TryAdd(DateTime.Now, mat))
                    Interlocked.Increment(ref frameQueueCount);
            }
            consumerSignal.Set();
        }

        private void HandleEncodedFrame(byte[] arg1, int arg2)
        {
            SendImageData(arg1, arg2);
        }
        
        private void HandleEncodedFrame2(Action<PooledMemoryStream> action, int byteLenght, bool isKeyFrame)
        {
            int howManyUnackedAllowed = (int)(AverageLatency / (1000 / Math.Max(1,actualFps))) + 2;
            int pending = Interlocked.CompareExchange(ref unAcked,0,0);
            if (pending > howManyUnackedAllowed)
            {
                RedcuceQuality(20 * (howManyUnackedAllowed - pending));
            }
            bytesSent += byteLenght;
            OnBytesAvailableAction?.Invoke(action,byteLenght,isKeyFrame);

        }

        Random rng = new Random();
        private void SendImageData(byte[] data, int length)
        {
            int howManyUnackedAllowed = (int)(AverageLatency / (1000/ Math.Max(1, actualFps))) + 2;
            int pending = Interlocked.CompareExchange(ref unAcked, 0, 0); ;
            if (timeDict.Count > howManyUnackedAllowed)
            {
                RedcuceQuality(5 * (howManyUnackedAllowed - pending));
            }
            //if (rng.Next(0, 100) % 15 == 0)
            //    return;

            //var dat = ByteCopy.ToArray(data, 0, length);
            //Task.Delay(rng.Next(0, 500)).ContinueWith((x) =>
            //{
            //    OnBytesAvailable?.Invoke(dat, length);
            //    bytesSent += length;

            //});

            bytesSent += length;
            OnBytesAvailable?.Invoke(data, length);
        }

        internal unsafe void HandleIncomingImage(DateTime timeStamp, byte[] payload, int payloadOffset, int payloadCount)
        {
            bytesReceived += payloadCount;
            consumerSignal.Set();
            transcoder.HandleIncomingFrame(timeStamp ,payload, payloadOffset, payloadCount);
        }
      

        internal void FlushBuffers()
        {
            frameQueue.Clear();

            while(MatPool.TryTake(out var mat))
                mat.Dispose();

            AverageLatency = 0;
            avgDivider = 2;
            timeDict.Clear();
            OnRemoteImageAvailable?.Invoke(null);
        }

        internal void Pause() => paused = true;
        internal void Resume() => paused = false;
       
        internal void HandleAck(MessageEnvelope message)
        {
            double currentLatency = 0;
            if (timeDict.TryRemove(message.MessageId, out var timeStamp))
            {
                currentLatency = (DateTime.Now - timeStamp).TotalMilliseconds;
                if(timeStamp>latestAck)
                    latestAck = timeStamp;
                Interlocked.Decrement(ref unAcked);

            }
            else
                return;

            if (AverageLatency == 0)
            {
                AverageLatency = currentLatency;
            }

            if (currentLatency <= AverageLatency)
                BumpQuality(2);
            else
            {
               // RedcuceQuality((int)Math.Abs(currentLatency - AverageLatency));
                //Console.WriteLine("recuced by latency");
            }
            AverageLatency = (avgDivider * AverageLatency + currentLatency) / (avgDivider + 1);
            avgDivider = Math.Min(600,avgDivider+1);

            // check for lost packets/jitter
            if (timeDict.Count > 0)
            {
                foreach (var item in timeDict)
                {
                    List<Guid> toRemove = new List<Guid>();
                    if ((DateTime.Now - item.Value).TotalMilliseconds > AverageLatency*1.3)
                    {
                        // lost package , we need to remove it and reduce quality after
                        toRemove.Add(item.Key);
                    }
                    foreach (var key in toRemove)
                    {
                        if(timeDict.TryRemove(key, out _))
                            Interlocked.Decrement(ref unAcked);
                      
                        RedcuceQuality(5);
                    }
                }

            }

        }
        internal void ImageDispatched(Guid messageId, DateTime timeStamp)
        {
            if(timeDict.TryAdd(messageId, timeStamp))
                Interlocked.Increment(ref unAcked);
        }
       
        private void RedcuceQuality(int factor=1)
        {
            if (factor < 0) return;
            if (!enableCongestionAvoidance) return;
            if(currentBps == 0)
            {
                currentBps = TargetBitrate;
            }
            var currentBps_ =currentBps - ( currentBps * factor / 100);
            currentBps = Math.Max(currentBps_, minBps);
            transcoder?.SetTargetBps(currentBps);
           
        }

        private void BumpQuality(int factor=1)
        {
            if (currentBps == 0)
            {
                currentBps = TargetBitrate;
            }
            var currentBps_ = currentBps+ ((currentBps * factor) / 100);
            currentBps = Math.Min(TargetBitrate, currentBps_);
            transcoder?.SetTargetBps(currentBps);
        }

        internal void ApplySettings(int camFrameWidth, int camFrameHeight,int targetBps, int idrInterval, int camIndex, string config)
        {
            if (camFrameHeight == 0 || camFrameWidth == 0)
                return;

            frameHeight = camFrameHeight;
            frameWidth = camFrameWidth;
            TargetBitrate = targetBps*1000;
            camIdx = camIndex;
            adjustCamSize = true;
            periodicKeyFrameInterval = idrInterval;
            transcoder?.SetKeyFrameInterval(periodicKeyFrameInterval);
            transcoder?.SetTargetBps((int)targetBps);

            if (config == "Default")
                configType = ConfigType.CameraBasic;
            else
                configType = ConfigType.CameraCaptureAdvanced;

            AverageLatency = 0; avgDivider = 0;
        }

        internal void ForceKeyFrame()
        {
            keyFrameRequested = true;
            RedcuceQuality(20);
        }
      
        public VCStatistics GetStatistics()
        {
            incomingFrameRate = Interlocked.Exchange(ref incomingFrameCount, 0);
            transcoder?.SetTargetFps(actualFps);

            var sendRate = (float)bytesSent / 1000;
            bytesSent = 0;
            var receiveRate = (float)bytesReceived / 1000;
            bytesReceived = 0;

            outgoingFrameRate = capturedFrameCnt;
            actualFps = outgoingFrameRate;
            capturedFrameCnt = 0;

            var st = new VCStatistics
            {
                OutgoingFrameRate = outgoingFrameRate,
                IncomingFrameRate = incomingFrameRate,
                TransferRate = sendRate,
                AverageLatency = AverageLatency,
                ReceiveRate = receiveRate,
                CurrentMaxBitRate = currentBps == 0 ? TargetBitrate : currentBps,
            };

            return st;
        }
    }
    struct VCStatistics
    {
        public float IncomingFrameRate;
        public float OutgoingFrameRate;
        public float TransferRate;
        public float ReceiveRate;
        public double AverageLatency;
        public int CurrentMaxBitRate;
        public override bool Equals(object obj) => obj is VCStatistics other && this.Equals(other);

        public bool Equals(VCStatistics p) => IncomingFrameRate == p.IncomingFrameRate
            && OutgoingFrameRate == p.OutgoingFrameRate
            && TransferRate == p.TransferRate
            && ReceiveRate == p.ReceiveRate
            && AverageLatency == p.AverageLatency;

        public override int GetHashCode() => (IncomingFrameRate, OutgoingFrameRate, TransferRate, AverageLatency).GetHashCode();

        public static bool operator ==(VCStatistics lhs, VCStatistics rhs) => lhs.Equals(rhs);

        public static bool operator !=(VCStatistics lhs, VCStatistics rhs) => !(lhs == rhs);
    }

}

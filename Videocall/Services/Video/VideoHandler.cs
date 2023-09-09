using NetworkLibrary;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Videocall
{
    //[ProtoContract]
    //class ImageMessage : IProtoMessage
    //{
    //    [ProtoMember(1)]
    //    public DateTime TimeStamp;

    //    [ProtoMember(2)]
    //    public byte[] Frame;
    //}
    //enum CompressionType
    //{
    //    Jpg = 0,
    //    Webp,
    //    Png
    //}
    //internal class VideoHandler
    //{
    //    //[DllImport("OpenCvSharpExtern", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    //    //public unsafe static extern IntPtr imgcodecs_imdecode_vector(byte* buf, IntPtr bufLength, int flags);

    //    public Action<byte[], Mat> OnCameraImageAvailable;
    //    public Action<Mat> OnNetworkFrameAvailable;
    //    public Action<int> QualityAutoAdjusted;
    //    public Action<float> SendRatePublished;
    //    public Action<double> AverageLatencyPublished;

    //    public int captureRateMs = 50;
    //    private int compressionLevel = 83;
    //    private int compressionLevel_ = 83;
    //    public bool EnableCongestionControl { get; set; } = false;
    //    public int VideoLatency { get; internal set; } = 200;
    //    public int AudioBufferLatency { get; internal set; } = 0;
    //    public double AverageLatency { get; private set; } = -1;
    //    internal CompressionType CompressionType
    //    {
    //        get => compressionType; set
    //        {
    //            compressionType = value;
    //            AverageLatency = 0;
    //            DeviationRtt = 0;
    //            avgDivider = 2;
    //        }
    //    }

    //    public int CompressionLevel
    //    {
    //        get => compressionLevel;
    //        set { compressionLevel = value; compressionLevel_ = value; QualityAutoAdjusted?.Invoke(value); }
    //    }

    //    public bool LimitPackageSize { get; set; } = false;

    //    private double DeviationRtt = 0;

    //    private VideoCapture capture;

    //    private readonly ConcurrentDictionary<DateTime, Mat> frameQueue = new ConcurrentDictionary<DateTime, Mat>();
    //    private readonly AutoResetEvent imgReady = new AutoResetEvent(false);
    //    private DateTime lastProcessedTimestamp = DateTime.Now;
    //    private CompressionType compressionType = CompressionType.Webp;
    //    private int frameCount;
    //    private int frameRate = 0;
    //    private int bytesSent = 0;
    //    private long avgDivider = 2;
    //    private readonly ConcurrentDictionary<Guid, DateTime> timeDict = new ConcurrentDictionary<Guid, DateTime>();
    //    private bool paused = false;
    //    private bool captureRunning = false;
    //    private object frameQlocker =  new object();
    //    private int frameWidth = 640;
    //    private int frameHeight = 480;
    //    private bool adjustCamsize = false;
    //    public VideoHandler()
    //    {
    //        QualityAutoAdjusted?.Invoke(compressionLevel_);
    //        // statistics
    //        Task.Run(async () =>
    //        {
    //            while (true)
    //            {
    //                await Task.Delay(1000);
    //                int count = Interlocked.Exchange(ref frameCount, 0);
    //                frameRate = Math.Max(count, 1);
    //                SendRatePublished?.Invoke((float)bytesSent / 1000);
    //                bytesSent = 0;
    //            }

    //        });
    //        // audio jitter syncronization
    //        Thread t = new Thread(() =>
    //        {
    //            while (true)
    //            {
    //                imgReady.WaitOne();
    //                while (frameQueue.Count > (VideoLatency + AudioBufferLatency) / (1000 / frameRate))
    //                {
    //                    KeyValuePair<DateTime, Mat> lastFrame;// oldest
    //                    lock (frameQlocker)
    //                    {
    //                        var samplesOrdered = frameQueue.OrderByDescending(x => x.Key);
    //                        lastFrame = samplesOrdered.Last();
    //                    }

    //                    if (lastProcessedTimestamp < lastFrame.Key)
    //                    {
    //                        OnNetworkFrameAvailable?.Invoke(lastFrame.Value);
    //                        lastProcessedTimestamp = lastFrame.Key;
    //                    }

    //                    frameQueue.TryRemove(lastFrame.Key, out _);

    //                }
    //            }

    //        });
    //        t.Priority = ThreadPriority.AboveNormal;
    //        t.Start();
    //    }

    //    public bool ObtainCamera()
    //    {
    //        if (capture != null && capture.IsOpened()) return true;

    //        capture = new VideoCapture(CaptureDevice.VFW, 0);
    //        capture.Open(0);
    //        // capture.Set(CaptureProperty.Fps, 60);
    //        //capture.FourCC = "YUY2";
    //        //capture.Sharpness = 0;
    //        //capture.Gain = 32;
    //        //capture.Gamma = 1/2.2;
    //        //capture.Saturation = 45;
    //        //   capture.Fps = 60;

    //        capture.FrameWidth = frameWidth;
    //        capture.FrameHeight = frameHeight;
    //        return capture.IsOpened();
    //    }

    //    public void CloseCamera()
    //    {
    //        if (capture != null && capture.IsOpened())
    //        {

    //            capture.Release();
    //            capture = null;
    //        }
    //    }
    //    ConcurrentBag<Mat> framePool = new ConcurrentBag<Mat>();
    //    public void StartCapturing()
    //    {
    //        if (captureRunning)
    //            return;
    //        captureRunning = true;
    //        var frame = new Mat();
    //        if (!capture.IsOpened())
    //        {
    //            if (!ObtainCamera())
    //                return;

    //        }
    //        int i = 0;  
    //        QualityAutoAdjusted?.Invoke(compressionLevel_);
    //        Thread t = new Thread(() =>
    //        {
    //            try
    //            {
    //                while (capture != null && capture.IsOpened())
    //                {
    //                    Thread.Sleep(Math.Max(0, captureRateMs - 16));
    //                    if (paused)
    //                        continue;
    //                    if (adjustCamsize)
    //                    {
    //                        capture.FrameHeight = frameHeight;
    //                        capture.FrameWidth = frameWidth;
    //                        frame =  new Mat();
    //                        adjustCamsize = false;
    //                    }
    //                    capture.Read(frame);
                        
    //                    ImageEncodingParam[] param;
    //                    string extention = "";
    //                    if (CompressionType == CompressionType.Webp)
    //                    {
    //                        extention = ".webp";
    //                        param = new ImageEncodingParam[1];
    //                        param[0] = new ImageEncodingParam(ImwriteFlags.WebPQuality, Clamp(40, 95, compressionLevel_));
    //                    }
    //                    else
    //                    {
    //                        extention = ".jpg";
    //                        param = new ImageEncodingParam[2];
    //                        param[0] = new ImageEncodingParam(ImwriteFlags.JpegQuality, Clamp(40, 95, compressionLevel_));
    //                        param[1] = new ImageEncodingParam(ImwriteFlags.JpegOptimize, 1);
    //                    }

    //                    try
    //                    {
    //                        var frameL = frame;
    //                        byte[] imageBytes;
    //                        imageBytes = frameL.ImEncode(ext: extention, param);
    //                       // File.WriteAllBytes(@"C:\Users\dcano\Desktop\Frames\" + i+++".jpg", imageBytes);
    //                        int imageByteSize = imageBytes.Length;
    //                        bytesSent += imageByteSize;

    //                        if (LimitPackageSize && imageByteSize > 62000)
    //                        {
    //                            compressionLevel_ -= 20;
    //                            CompressionLevel--;
    //                            QualityAutoAdjusted?.Invoke(compressionLevel_);

    //                            return;
    //                        }

    //                        OnCameraImageAvailable?.Invoke(imageBytes, frameL);
    //                        framePool.Add(frameL);
    //                    }
    //                    catch (Exception ex) { DebugLogWindow.AppendLog(" Capture encoding failed: ", ex.Message); };
    //                }
    //            }
    //            finally
    //            {
    //                captureRunning = false;
    //                CloseCamera();
    //            }

    //        });
    //        t.Start();


    //    }
    //    private int Clamp(int low, int high, int val)
    //    {
    //        if (val > high) return high;
    //        else if (val < low) return low;
    //        return val;
    //    }

    //    internal void HandleIncomingImage(DateTime timeStamp, byte[] payload, int payloadOffset, int payloadCount)
    //    {
    //        var buffa = BufferPool.RentBuffer(payloadCount);
    //        Buffer.BlockCopy(payload, payloadOffset, buffa, 0, payloadCount);

    //        //ThreadPool.UnsafeQueueUserWorkItem((x) =>
    //        //{
    //        // var buff = (byte[])x;
    //        var buff = buffa;
    //            Mat img = new Mat(NativeMethods.imgcodecs_imdecode_vector(buff, new IntPtr(payloadCount), (int)ImreadModes.Color));
    //            BufferPool.ReturnBuffer(buff);

    //            lock(frameQlocker)
    //                frameQueue[timeStamp] = img;

    //            imgReady.Set();
    //            Interlocked.Increment(ref frameCount);
    //       // }, buffa);


    //    }

    //    internal void FlushBuffers()
    //    {
    //        frameQueue.Clear();
    //        AverageLatency = 0;
    //        DeviationRtt = 0;
    //        avgDivider = 2;
    //        timeDict.Clear();
    //    }

    //    internal void Pause() => paused = true;
    //    internal void Resume() => paused = false;

    //    internal void HandleAck(MessageEnvelope message)
    //    {
    //        double currentLatency = 0;
    //        if (timeDict.TryRemove(message.MessageId, out var timeStamp))
    //        {
    //            currentLatency = (DateTime.Now - timeStamp).TotalMilliseconds;
    //        }
    //        else
    //            return;

    //        if (AverageLatency == 0)
    //        {
    //            AverageLatency = currentLatency;
    //        }

    //        if (currentLatency <= AverageLatency)
    //            BumpQuality();
    //        else
    //            RecuceQuality();
    //        AverageLatency = (avgDivider * AverageLatency + currentLatency) / (avgDivider + 1);
    //        avgDivider++;

    //        AverageLatencyPublished?.Invoke(AverageLatency);
    //        // check for lost packets
    //        if (timeDict.Count > 0)
    //        {
    //            foreach (var item in timeDict)
    //            {
    //                List<Guid> toRemove = new List<Guid>();
    //                if ((DateTime.Now - item.Value).TotalMilliseconds > AverageLatency * 2)
    //                {
    //                    // lost package , we need to remove it and reduce quality after
    //                    toRemove.Add(item.Key);
    //                }
    //                foreach (var key in toRemove)
    //                {
    //                    timeDict.TryRemove(key, out _);
    //                    RecuceQuality(10);
    //                }
    //            }

    //        }

    //    }

    //    private void RecuceQuality(int reduction = 1)
    //    {
    //        if (!EnableCongestionControl) return;

    //        int old = compressionLevel_;
    //        int compressionTarget = compressionLevel_ - reduction;
    //        compressionLevel_ = Math.Max(40, compressionTarget);

    //        if (old != compressionLevel_)
    //            QualityAutoAdjusted?.Invoke(compressionLevel_);
    //    }

    //    private void BumpQuality(int bump = 5)
    //    {
    //        if (!EnableCongestionControl) return;

    //        int old = compressionLevel_;
    //        var compressionTarget = compressionLevel_ + bump;
    //        compressionLevel_ = Math.Min(CompressionLevel, compressionTarget);

    //        if (old != compressionLevel_)
    //            QualityAutoAdjusted?.Invoke(compressionLevel_);

    //    }

    //    // when we dispatrch image to remote, we need to timestamp it and compare on acks arrival
    //    internal void ImageDispatched(Guid messageId, DateTime timeStamp)
    //    {
    //        timeDict.TryAdd(messageId, timeStamp);
    //    }

    //    internal void ApplySettings(int camFrameWidth, int camFrameHeight)
    //    {
    //        if(camFrameHeight == 0 || camFrameWidth == 0)
    //            return;
    //        frameHeight = camFrameHeight;
    //        frameWidth= camFrameWidth;
    //        adjustCamsize = true;
    //    }
    //}
}

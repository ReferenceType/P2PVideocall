using OpenCvSharp;
using OpenCvSharp.Extensions;
using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    enum CompressionType
    {
        Jpg = 0,
        Webp,
        Png
    }
    internal class VideoHandler
    {
        public CompressionType compressionType = CompressionType.Webp;
        public Action<byte[],Mat> OnCameraImageAvailable;
        public Action<Mat> OnNetworkFrameAvailable;
        public Action<int> QualityAutoAdjusted;
        public Action<float> SendRatePublished;
        public Action<double> AverageLatencyPublished;

        public int captureRateMs = 50;
        public int CompressionLevel = 83;
        private int compressionLevel_ = 83;

        public int VideoLatency { get; internal set; } = 200;
        public int AudioBufferLatency { get; internal set; } = 0;
        public double AverageLatency { get; private set; } = -1;

        private VideoCapture capture;

        private readonly ConcurrentDictionary<DateTime,Mat> frameQueue = new ConcurrentDictionary<DateTime, Mat>();
        private readonly AutoResetEvent imgReady = new AutoResetEvent(false);
        private DateTime lastProcessedTimestamp = DateTime.Now;

        private int frameCount;
        private int frameRate = 0;
        private int bytesSent = 0;

        private long avgDivider = 2;
        private readonly ConcurrentDictionary<Guid,DateTime> timeDict = new ConcurrentDictionary<Guid,DateTime>();   

        private bool paused= false;
        private bool captureRunning = false;


        public VideoHandler()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(1000);
                    int count = Interlocked.Exchange(ref frameCount, 0);
                    frameRate = Math.Max(count,1);
                    SendRatePublished?.Invoke((float)bytesSent / 1000);
                    bytesSent= 0;
                }
               
            });
            Thread t = new Thread(() =>
            {
                while (true)
                {
                    imgReady.WaitOne();
                    while(frameQueue.Count > (VideoLatency+AudioBufferLatency)/(1000/frameRate))
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
                    while (capture!= null && capture.IsOpened())
                    {
                        Thread.Sleep(Math.Max(0, captureRateMs - 16));
                        if (paused)
                            continue;

                        capture.Read(frame);
                        ImageEncodingParam[] param;
                        string extention = "";
                        if (compressionType == CompressionType.Webp)
                        {
                            extention = ".webp";
                            param = new ImageEncodingParam[1];
                            param[0] = new ImageEncodingParam(ImwriteFlags.WebPQuality, Clamp(10, 95, compressionLevel_));
                        }
                        else 
                        {
                            extention = ".jpg";
                            param = new ImageEncodingParam[2];
                            param[0] = new ImageEncodingParam(ImwriteFlags.JpegQuality, Clamp(10, 95, compressionLevel_));
                            param[1] = new ImageEncodingParam(ImwriteFlags.JpegOptimize, 1);
                        }
                        

                        
                      
                        byte[] imageBytes;
                        try
                        {
                            imageBytes = frame.ImEncode(ext: extention, param);
                            //imageBytes = frame.ImEncode(ext: ".jpg", param);
                            int imageByteSize = imageBytes.Length;
                            bytesSent += imageByteSize;

                            if (imageByteSize > 62000)
                            {
                                compressionLevel_ -= 20;
                                CompressionLevel--;
                                QualityAutoAdjusted?.Invoke(compressionLevel_);

                                continue;
                            }

                            OnCameraImageAvailable?.Invoke(imageBytes,frame);
                        }
                        catch { break; }

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
        private int Clamp(int low, int high, int val)
        {
            if (val > high) return high;
            else if (val < low) return low;
            return val;
        }

        internal void HandleIncomingImage(DateTime timeStamp,byte[] payload)
        {
            Mat img = Cv2.ImDecode(payload, ImreadModes.Unchanged);
            frameQueue[timeStamp] = img;
            imgReady.Set();
            Interlocked.Increment(ref frameCount);
        }

        internal void FlushBuffers()
        {
            frameQueue.Clear();
            AverageLatency= -1;
            avgDivider = 2;
            timeDict.Clear();
        }

        internal void Pause() =>paused= true;
        internal void Resume() =>paused= false;

        internal void HandleAck(MessageEnvelope message)
        {
            double currentLatency = 0;
            if (timeDict.TryRemove(message.MessageId, out var timeStamp))
            {
                 currentLatency = (DateTime.Now - timeStamp).TotalMilliseconds;
            }
            else
                return;

            if (AverageLatency == -1)
            {
                AverageLatency = currentLatency;
            }

            if (currentLatency <= AverageLatency)
                BumpQuality();
            else
                RecuceQuality();

            AverageLatency = (avgDivider * AverageLatency + currentLatency) / (avgDivider+1);
            avgDivider++;

            AverageLatencyPublished?.Invoke(AverageLatency);
            // check for lost packets
            if (timeDict.Count>0)
            {
                foreach (var item in timeDict)
                {
                    List<Guid> toRemove = new List<Guid>();
                    if((DateTime.Now - item.Value).TotalMilliseconds>AverageLatency*2)
                    {
                        // lost package , we need to remove it and reduce quality after
                        toRemove.Add(item.Key);
                    }
                    foreach (var key in toRemove)
                    {
                        timeDict.TryRemove(key, out _);
                        RecuceQuality(10);
                    }
                }

            }

        }

        private void RecuceQuality(int reduction = 5)
        {
            int old = compressionLevel_;
            int compressionTarget = compressionLevel_ - reduction;
            compressionLevel_ = Math.Max(10, compressionTarget);

            if (old != compressionLevel_)
                QualityAutoAdjusted?.Invoke(compressionLevel_);
        }

        private void BumpQuality()
        {
            int old = compressionLevel_;
            var compressionTarget = compressionLevel_ + 5;
            compressionLevel_ = Math.Min(CompressionLevel, compressionTarget);

            if (old != compressionLevel_)
                QualityAutoAdjusted?.Invoke(compressionLevel_);

        }

        // when we dispatrch imageto remote, we need to timestamp it and compare on acks arrival
        internal void ImageDispatched(Guid messageId, DateTime timeStamp)
        {
            timeDict.TryAdd(messageId, timeStamp);
        }
    }
}

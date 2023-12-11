using NetworkLibrary;
using NetworkLibrary.Components;
using OpenCvSharp;
using H264Sharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static H264Sharp.Encoder;
using System.Windows.Media.Animation;

namespace Videocall.Services.Video.H264
{
   
    internal class H264Transcoder
    {
        public Action<Action<PooledMemoryStream>, int,bool> EncodedFrameAvailable2;
        public Action<byte[], int, bool> EncodedFrameAvailable;
        public Action<Mat> DecodedFrameAvailable;
        public Action KeyFrameRequested;
        public ConcurrentBag<Mat> matPool;
        private bool DecodeNoDelay = true;
        public bool InvokeAction => EncodedFrameAvailable2!=null;
        public double Duration => jitterBufffer.Duration;
        public ushort encoderWidth { get; private set; }
        public ushort encoderHeight { get; private set; }

        private H264Sharp.Encoder encoder;
        private H264Sharp.Decoder decoder;
        private JitterBufffer jitterBufffer = new JitterBufffer();
        private object incrementLocker = new object();
        private object changeLock = new object();
        private byte[] cache = new byte[64000];
        private int keyReq=0;
        private int fps;
        private int bps;
        private int frameCnt = 0;
        private int keyFrameInterval = -1;
        private int bytesSent = 0;
        private ushort seqNo = 0;
        private ushort decoderWidth;
        private ushort decoderHeight;
    
        public H264Transcoder(ConcurrentBag<Mat> matPool,int desiredFps,int desiredBps)
        {
            this.matPool = matPool;
            fps = desiredFps;
            bps = desiredBps;
            jitterBufffer.FrameAvailable += (f) => Decode(f.Data, f.Offset, f.Count);
        }
        public H264Transcoder(int desiredFps, int desiredBps)
        {
            fps = desiredFps;
            bps = desiredBps;
            jitterBufffer.FrameAvailable += (f) => Decode2(f.Data, f.Offset, f.Count);
        }

        public unsafe void SetupTranscoder(int encoderWidth, int encoderHeight, ConfigType configType = ConfigType.CameraBasic)
        {
            if (encoder != null)
            {
                encoder.Dispose();
            }
            encoder = H264TranscoderProvider.CreateEncoder(encoderWidth, encoderHeight, fps: fps, bps: bps, configType);
            if (decoder == null)
                decoder = H264TranscoderProvider.CreateDecoder();
            this.encoderWidth =(ushort) encoderWidth;
            this.encoderHeight = (ushort)encoderHeight;
        }

        private void DisposeTranscoder()
        {
            var encoder = this.encoder;
            this.encoder = null;
            encoder.Dispose();
            var decoder = this.decoder;
            this.decoder = null;
            decoder.Dispose();

        }

        private void OnEncoded(EncodedFrame[] frames )
        {
            int length = 0;
            bool isKeyFrame = false;
            foreach (var f in frames)
            {
                length += f.Length;
                if (f.Type == FrameType.IDR || f.Type == FrameType.I)
                {
                    isKeyFrame = true;
                }
            }
            if (length == 0)
                return;
            bytesSent += length;
            if (!InvokeAction)
                PublishFrame(frames, length, isKeyFrame);
            else
                PublishFrameWithAction(frames, length, isKeyFrame);

        }

        private void PublishFrame(EncodedFrame[] frames, int length, bool isKeyFrame)
        {

            if (cache.Length < length*2)
                cache = new byte[length*2];

            int offset = 0;
            WriteMetadata(cache, ref offset);
            foreach (var frame in frames)
            {
                frame.CopyTo(cache, offset);
                offset += frame.Length;
            }

            EncodedFrameAvailable?.Invoke(cache, length+offset, isKeyFrame);

        }

        // This black magic writes directly to socket buffer
        private void PublishFrameWithAction(EncodedFrame[] frames, int length, bool isKeyFrame)
        {
            EncodedFrameAvailable2?.Invoke( Stream => 
            {
                Stream.Reserve(length + 50);
                var cache = Stream.GetBuffer();
                int offset = Stream.Position32;
                
                WriteMetadata(cache, ref offset);
                foreach(var frame in frames)
                {
                    frame.CopyTo(cache, offset);
                    offset += frame.Length;
                }
                Stream.Position32 = offset;
            }, length, isKeyFrame);
            

        }

        private void WriteMetadata(byte[] cache, ref int offset)
        {
            lock (incrementLocker)// its uint16 no interlocked support
                seqNo++;
            PrimitiveEncoder.WriteFixedUint16(cache, offset, seqNo);
            PrimitiveEncoder.WriteFixedUint16(cache, offset+2, encoderWidth);
            PrimitiveEncoder.WriteFixedUint16(cache, offset+4, encoderHeight);
            offset+= 6;

        }
        public unsafe void Encode(BgraImage bgra)
        {
            if (keyFrameInterval != -1 && frameCnt++ > keyFrameInterval)
            {
                lock (changeLock)
                    encoder.ForceIntraFrame();
                frameCnt = 0;
            }

            lock (changeLock)
            {
                if (encoder.Encode(bgra, out EncodedFrame[] frames))
                {
                      OnEncoded(frames);
                }
            }
        }
        public void Encode(BgrImage bgr)
        {
            if (keyFrameInterval != -1 && frameCnt++ > keyFrameInterval)
            {
                lock (changeLock)
                    encoder.ForceIntraFrame();
                frameCnt = 0;
            }

            lock (changeLock)
            {
                if (encoder.Encode(bgr, out EncodedFrame[] frames))
                {
                     OnEncoded(frames);
                }
            }
        }
        public void Encode(RgbImage rgb)
        {
            if (keyFrameInterval != -1 && frameCnt++ > keyFrameInterval)
            {
                lock (changeLock)
                    encoder.ForceIntraFrame();
                frameCnt = 0;
            }

            lock (changeLock)
            {
                if (encoder.Encode(rgb, out EncodedFrame[] frames))
                {
                      OnEncoded(frames);
                }
            }
        }
        public unsafe void Encode(byte* Yuv420p)
        {
            if(keyFrameInterval != -1 && frameCnt++ >keyFrameInterval)
            {
                lock (changeLock)
                    encoder.ForceIntraFrame();
                frameCnt = 0;
            }

            lock (changeLock)
            {
                if (encoder.Encode(Yuv420p, out EncodedFrame[] frames))
                {
                     OnEncoded(frames);
                }
            }
        }
        public void Encode(Bitmap bmp)
        {
            if (keyFrameInterval!= -1 && frameCnt++ > keyFrameInterval)
            {
                lock (changeLock)
                    encoder.ForceIntraFrame();
                frameCnt = 0;
            }
           
            lock (changeLock)
            {
                if (encoder.Encode(bmp,out EncodedFrame[] frames))
                {
                        OnEncoded(frames);
                }
            }
               
        }
        internal void ForceIntraFrame()
        {
            lock (changeLock)
                encoder.ForceIntraFrame();
        }
        public void SetKeyFrameInterval(int everyNFrame)
        {
            keyFrameInterval = everyNFrame;
        }

        public void SetTargetBps(int bps)
        {
            lock (changeLock)
                encoder?.SetMaxBitrate(bps);
        }
       public void SetTargetFps(float fps)
        {
            lock (changeLock)
                encoder?.SetTargetFps(fps);
        }
        // Decode
        // Goes to jitter, jitter publishes, then decode is called.
        internal unsafe void HandleIncomingFrame( DateTime timeStamp, byte[] payload, int payloadOffset, int payloadCount)
        {
            ushort sqn = BitConverter.ToUInt16(payload, payloadOffset);
            ushort w = BitConverter.ToUInt16(payload, payloadOffset+2);
            ushort h = BitConverter.ToUInt16(payload, payloadOffset+4);
            if(encoderWidth == 0)
            {
                decoderWidth = w; decoderHeight = h;

            }
            if (decoderWidth !=w || decoderHeight != h)
            {
                lock (changeLock)
                {
                    decoderWidth = w; 
                    decoderHeight = h;
                    decoder.Dispose();
                    decoder = H264TranscoderProvider.CreateDecoder();
                    jitterBufffer.Reset();
                }
               
            }
            payloadOffset += 6;
            payloadCount-= 6;

            jitterBufffer.HandleFrame(timeStamp, sqn, payload, payloadOffset, payloadCount);
        }
        private unsafe void Decode(byte[] payload, int payloadOffset, int payloadCount)
        {
            lock (changeLock)
            {
                fixed (byte* b = &payload[payloadOffset])
                {
                    if (decoder == null)
                        decoder = H264TranscoderProvider.CreateDecoder();

                    Mat mainMat = null;
                    try
                    {
                        if (decoder.Decode(b, payloadCount, noDelay: DecodeNoDelay, out DecodingState statusCode, out RgbImage rgbImg))
                        {
                            var ptr = new IntPtr(rgbImg.ImageBytes);
                            var mat = new Mat(rgbImg.Height, rgbImg.Width, MatType.CV_8UC3, ptr);

                            mainMat = mat;
                            if (matPool.TryTake(out var pooledMat)) 
                            { 

                                try
                                {
                                    //TODO check size, bounds etc..
                                    if (pooledMat.Size() == mat.Size() && !pooledMat.IsDisposed)
                                        mat.CopyTo(pooledMat);
                                    else
                                    {
                                        // let the gc handle it, it can be attached to canvas.
                                        //pooledMat.Dispose();
                                        pooledMat = mat.Clone();
                                    }
                                }
                                catch { pooledMat = mat.Clone(); }
                                

                                mainMat = pooledMat;
                            }
                            else
                            {
                                mainMat = mat.Clone();
                            }
                            keyReq = 0;
                        }


                        if (statusCode != 0)
                        {
                            if (--keyReq < 0)
                            {
#if Debug
                                Console.WriteLine("KeyFrameRequested");
#endif
                                //jitterBufffer.Discard();
                                KeyFrameRequested?.Invoke();
                                keyReq = 3;
                            }

                        }
                    }
                    catch (Exception e)
                    {
                        decoder.Dispose();
                        decoder = null;
#if Debug
                        Console.WriteLine("DecoderBroken: " + e.Message);
#endif
                        return;
                    }
                    if (mainMat != null)
                    {
                        DecodedFrameAvailable?.Invoke(mainMat);
                    }
                    else
                    {
                       // Console.WriteLine("Bmp null");
                    }
                }
            }
        }
        private unsafe void Decode2(byte[] payload, int payloadOffset, int payloadCount)
        {
            lock (changeLock)
            {
                fixed (byte* b = &payload[payloadOffset])
                {
                    if (decoder == null)
                        decoder = H264TranscoderProvider.CreateDecoder();

                    Mat mainMat = null;
                    try
                    {
                        if (decoder.Decode(b, payloadCount, noDelay: DecodeNoDelay, out DecodingState statusCode, out RgbImage rgbImg))
                        {
                            var ptr = new IntPtr(rgbImg.ImageBytes);
                            var mat = new Mat(rgbImg.Height, rgbImg.Width, MatType.CV_8UC3, ptr);

                            mainMat = mat;
                            keyReq = 0;
                        }

                        if (statusCode != DecodingState.dsErrorFree)
                        {
                            
                            if (--keyReq < 0)
                            {
#if Debug
                                Console.WriteLine("KeyFrameRequested");
#endif
                                jitterBufffer.Discard();
                                KeyFrameRequested?.Invoke();
                                keyReq = 5;
                            }

                        }
                    }
                    catch (Exception e)
                    {
                        decoder.Dispose();
                        decoder = null;
#if Debug
                        Console.WriteLine("DecoderBroken: " + e.Message);
#endif
                        return;
                    }
                    if (mainMat != null)
                    {
                        DecodedFrameAvailable?.Invoke(mainMat);
                    }
                    else
                    {
#if Debug
                        Console.WriteLine("Bmp null");
#endif
                    }
                }
            }
        }
        
        internal void ApplyChanges(int fps, int targetBitrate, int frameWidth, int frameHeight, ConfigType configType = ConfigType.CameraBasic)
        {
            lock (changeLock)
            {
                encoderWidth = (ushort)frameWidth;
                encoderHeight = (ushort)frameHeight;
                encoder.Dispose();
                unsafe
                {
                    encoder = H264TranscoderProvider.CreateEncoder(frameWidth, frameHeight, fps: fps, bps: targetBitrate, configType);
                }
            }
            

        }
    }
}

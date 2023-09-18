using NetworkLibrary;
using NetworkLibrary.Utils;
using ProtoBuf.WellKnownTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Videocall.Services.Video.H264
{
    class Frame
    {
        public DateTime TimeStamp;
        public byte[] Data;
        public int Offset;
        public int Count;
    }
    internal class JitterBufffer
    {
        public Action<Frame> FrameAvailable;
        ConcurrentDictionary<ushort, Frame> reorderBuffer = new ConcurrentDictionary<ushort, Frame>();
        DateTime lastStamp = DateTime.Now;
        DateTime latestTs = DateTime.Now;
        DateTime oldestTs = DateTime.Now;
        public double Duration => ((latestTs - oldestTs).TotalMilliseconds);
        private ushort prevSqn;
        private readonly object locker = new object();
        DateTime lastIn = DateTime.Now;
        public int MaxNumberOfFramesBuffered = 5;
        Stopwatch sw =  new Stopwatch();
        int incomingFrameCount = 0;
        public void HandleFrame(DateTime timeStamp, ushort currentSqn, byte[] payload, int payloadOffset, int payloadCount)
        {
            if (latestTs < timeStamp)
            {
                latestTs = timeStamp;
            }
            lock (locker)
            {
                if (!sw.IsRunning)
                {
                    sw.Start();
                }
                incomingFrameCount++;
                if (sw.ElapsedMilliseconds > 1000)
                {
                    MaxNumberOfFramesBuffered = Math.Max(2, (incomingFrameCount / 4));//250ms
                    incomingFrameCount = 0;
                    sw.Restart();
                }


                var buffer = BufferPool.RentBuffer(payloadCount);
                ByteCopy.BlockCopy(payload, payloadOffset, buffer, 0, payloadCount);

                Frame f = new Frame { Data = buffer, Count = payloadCount, TimeStamp = timeStamp };
                reorderBuffer.TryAdd(currentSqn, f);
                var now = DateTime.Now;
                if((now-lastIn).TotalMilliseconds<20 && currentSqn != prevSqn + 1 && reorderBuffer.Count > MaxNumberOfFramesBuffered)
                {
                    Console.WriteLine("--  Video Buff Forced");

                    lastIn = now;
                    return;
                }
                lastIn = now;
                while (currentSqn == prevSqn + 1 || reorderBuffer.Count > MaxNumberOfFramesBuffered)
                {
                    var key = reorderBuffer.Keys.Min();
                   
                    if (reorderBuffer.TryRemove(key, out var ff))
                    {
                        oldestTs = ff.TimeStamp;
                        FrameAvailable?.Invoke(ff);
                        BufferPool.ReturnBuffer(ff.Data);
                    }
                    lastStamp = timeStamp;
                    prevSqn = key;

                    var next = (ushort)(prevSqn + 1);
                    if (reorderBuffer.ContainsKey(next))
                    {
                        currentSqn= next;
                    }

                }
                //Console.WriteLine("Dur " + Duration);
                //Console.WriteLine("CCC " + reorderBuffer.Count);
            }
        }

        public void Discard()
        {
            reorderBuffer.Clear();
            oldestTs = lastStamp;
        }
        public void Reset()
        {
            prevSqn = 0;
            reorderBuffer.Clear();
            oldestTs = lastStamp;

        }
    }
}

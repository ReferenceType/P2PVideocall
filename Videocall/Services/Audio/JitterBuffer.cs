using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;

namespace Videocall
{
    class JitterBuffer
    {
        public Action<byte[], int, int> OnSamplesCollected;
        public int NumLostPackages = 0;
        public int BufferLatency;
        public int MinBufferLatency = 60;
        public int Duration => numSeqBuffered * 20;
        private ConcurrentDictionary<DateTime, AudioSample> samples = new ConcurrentDictionary<DateTime, AudioSample>();
        private readonly object locker = new object();
        private MemoryStream sampleStream = new MemoryStream();
        private DateTime lastBatchTimeStamp = DateTime.Now;
        private int numSeqBuffered = 0;
        private AutoResetEvent bufferFullEvent = new AutoResetEvent(false);
        private DateTime lastIn = DateTime.Now;

        public JitterBuffer(int bufferLatency)
        {
            this.BufferLatency = bufferLatency;
            StartPublushing2();

        }
        // publish if anything is in buffer.
        public void StartPublushing2()
        {
            Thread t = new Thread(() =>
            {
                ushort lastSeq = 0;
                while (true)
                {
                    bufferFullEvent.WaitOne();
                    KeyValuePair<DateTime, AudioSample>[] samplesOrdered_;
                    lock (locker)
                    {
                        samplesOrdered_ = samples.OrderByDescending(x => x.Key).Reverse().ToArray();
                    }
                    // iterate and count all consecutive sequences.
                    int consequtiveSeqLenght = 0;
                    for (int i = 0; i < samplesOrdered_.Length - 1; i++)
                    {
                        if (samplesOrdered_[i].Value.SquenceNumber + 1 == samplesOrdered_[i + 1].Value.SquenceNumber)
                            consequtiveSeqLenght++;
                        else break;
                    }
                    //int toTake = Math.Min(2, numSeqBuffered) + (samplesOrdered_.Count() - (BufferLatency / 20));
                    IEnumerable<KeyValuePair<DateTime, AudioSample>> samplesOrdered;
                    // jittr buffer reached max duration, we have to take even if seq is broken
                    if (numSeqBuffered >= BufferLatency / 20)
                    {
                        int toTake = Math.Max(2, consequtiveSeqLenght+1);
                        samplesOrdered = samplesOrdered_.Take(Math.Min(toTake, samplesOrdered_.Count()));
                    }
                    // keep buffering seq isnt complete
                    else if (consequtiveSeqLenght == 0)
                    {
                        continue;
                    }
                    //key point is here, if its continous sequence we take else we buffer
                    else if (lastSeq == samplesOrdered_.First().Value.SquenceNumber - 1)
                    {
                        int toTake = Math.Max(2, consequtiveSeqLenght+1); // this was without max
                        samplesOrdered = samplesOrdered_.Take(Math.Min(toTake, samplesOrdered_.Count()));
                    }
                    else continue;

                    lastBatchTimeStamp = samplesOrdered.Last().Key;

                    var sampArry = samplesOrdered.ToImmutableArray();
                    for (int i = 0; i < sampArry.Length - 1; i++)
                    {

                        if (sampArry[i].Value.SquenceNumber + 1 == sampArry[i + 1].Value.SquenceNumber)
                        {
                            sampleStream.Write(sampArry[i].Value.Data, 0, sampArry[i].Value.DataLenght);
                            lastSeq = sampArry[i].Value.SquenceNumber;
                        }
                        // lost packet we conceal them here
                        else
                        {
                            //delta is at least 2 here
                            int delta = sampArry[i + 1].Value.SquenceNumber - sampArry[i].Value.SquenceNumber;
                            for (int j = 0; j < delta - 1; j++)
                            {
                                sampleStream.Write(sampArry[i].Value.Data, 0, sampArry[i].Value.DataLenght);
                                NumLostPackages++;
                            }
                        }

                        lock (locker)
                        {
                            samples.TryRemove(sampArry[i].Key, out _);
                            numSeqBuffered--;
                        } 

                    }

                    try
                    {
                        OnSamplesCollected?.Invoke(sampleStream.GetBuffer(), 0, (int)sampleStream.Position);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }

                    sampleStream.Position = 0;


                }
            });
            t.Priority = ThreadPriority.AboveNormal;
            t.Start();
        }
        public void AddSample(AudioSample sample)
        {
            // special debug case, it cant be with gui slider.
            if (BufferLatency < 60)
            {
                OnSamplesCollected?.Invoke(sample.Data, 0, sample.DataLenght);
                return;
            }

            lock (locker)
            {
                if (!samples.ContainsKey(sample.Timestamp) && sample.Timestamp >= lastBatchTimeStamp)
                {
                    samples.TryAdd(sample.Timestamp, sample);
                    numSeqBuffered++;
                    var now = DateTime.Now;
                    if ((now - lastIn).TotalMilliseconds < 10 && numSeqBuffered>4)
                    {
                        Console.WriteLine("Audio Buff Forced");
                        lastIn = now;
                        return;
                        
                    }
                    lastIn = now;
                    if (numSeqBuffered >= 2)
                    {
                        bufferFullEvent.Set();
                    }
                }
            }
        }

        public void DiscardSamples(int num)
        {
            lock (locker)
            {
                var samplesOrdered = samples.OrderByDescending(x => x.Key).Reverse();
                //samplesOrdered = samplesOrdered.Take(samplesOrdered.Count() - 10);
                samplesOrdered = samplesOrdered.Take(Math.Min(num, samplesOrdered.Count()));

                foreach (var item in samplesOrdered)
                {
                    samples.TryRemove(item.Key, out _);
                    numSeqBuffered--;

                }
            }

        }


    }
}


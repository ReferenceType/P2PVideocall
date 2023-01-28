using NAudio.Wave;
using System;
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

        private Dictionary<DateTime,AudioSample> samples = new Dictionary<DateTime, AudioSample>();
        private readonly object locker = new object();
        private MemoryStream sampleStream = new MemoryStream();
        private DateTime lastBatchTimeStamp = DateTime.Now;
        private int numSeqBuffered = 0;
        private AutoResetEvent bufferFullEvent = new AutoResetEvent(false); 

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
                while (true)
                {
                    bufferFullEvent.WaitOne();
                    lock (locker)
                    {
                        var samplesOrdered = samples.OrderByDescending(x => x.Key).Reverse();
                        int toTake = Math.Min(2, numSeqBuffered) + (samplesOrdered.Count() - (BufferLatency / 20));
                        samplesOrdered = samplesOrdered.Take(Math.Min(toTake, samplesOrdered.Count()));

                        lastBatchTimeStamp = samplesOrdered.Last().Key;

                        var sampArry = samplesOrdered.ToImmutableArray();
                        for (int i = 0; i < sampArry.Length - 1; i++)
                        {
                           
                            if (sampArry[i].Value.SquenceNumber + 1 == sampArry[i + 1].Value.SquenceNumber)
                            {
                                sampleStream.Write(sampArry[i].Value.Data, 0, sampArry[i].Value.Data.Length);
                            }
                            // lost packets we conceal them here
                            else
                            {
                                int delta = sampArry[i + 1].Value.SquenceNumber - sampArry[i].Value.SquenceNumber;
                                for (int j = 0; j < delta - 1; j++)
                                {
                                    sampleStream.Write(sampArry[i].Value.Data, 0, sampArry[i].Value.Data.Length);
                                    NumLostPackages++;
                                    Console.WriteLine("Drop");
                                }


                            }
                            samples.Remove(sampArry[i].Key);
                            numSeqBuffered--;

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

                }
            });
            t.Priority = ThreadPriority.AboveNormal;
            t.Start();
        }
        public void AddSample(AudioSample sample)
        {
            lock (locker)
            {

                if (!samples.ContainsKey(sample.Timestamp) && sample.Timestamp > lastBatchTimeStamp)
                {
                    samples.Add(sample.Timestamp, sample);
                    numSeqBuffered++;
                    if(numSeqBuffered >= BufferLatency/20)
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
                    samples.Remove(item.Key);
                    numSeqBuffered--;

                }
            }
               
        }

       
    }
}

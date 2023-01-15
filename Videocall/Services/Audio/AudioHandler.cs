using NAudio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NetworkLibrary.Utils;
using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Videocall
{
    [ProtoContract]
    class AudioSample: IProtoMessage
    {
        [ProtoMember(1)]
        public DateTime Timestamp;

        [ProtoMember(2)]
        public ushort SquenceNumber;
       
        [ProtoMember(3)]
        public byte[] Data;
    }

    public struct AudioStatistics
    {
        public int BufferSize;
        public int BufferedDuration;
        public int NumLostPackages;
        public int TotalNumDroppedPAckages;
    }
    class JitterBuffer
    {
        private Dictionary<DateTime,AudioSample> samples = new Dictionary<DateTime, AudioSample>();
        readonly object locker = new object();
        private MemoryStream sampleStream = new MemoryStream();
        public Action<byte[], int, int> OnSamplesCollected;
        private DateTime LastBatchTimeStamp = DateTime.Now;
        public int BufferLatency;
        private int NumSqBuffered = 0;
        private AutoResetEvent bufferFullEvent = new AutoResetEvent(false); 
        public int NumLostPackages = 0;

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
                        //if (samples.Count < 11)
                        //    continue;


                        var samplesOrdered = samples.OrderByDescending(x => x.Key).Reverse();
                        //samplesOrdered = samplesOrdered.Take(samplesOrdered.Count() - 10);
                        int toTake = Math.Min(2, NumSqBuffered) + (samplesOrdered.Count() - (BufferLatency / 20));
                        samplesOrdered = samplesOrdered.Take(Math.Min(toTake, samplesOrdered.Count()));

                        LastBatchTimeStamp = samplesOrdered.Last().Key;

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
                            NumSqBuffered--;

                        }
                        //if (numDrops > 2)
                        //{
                        //    BufferLatency += 20;
                        //    Console.WriteLine("Increased");

                        //}


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

                if (!samples.ContainsKey(sample.Timestamp) && sample.Timestamp > LastBatchTimeStamp)
                {
                    samples.Add(sample.Timestamp, sample);
                    NumSqBuffered++;
                    if(NumSqBuffered >= BufferLatency/20)
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
                    NumSqBuffered--;

                }
            }
               
        }

       
    }
    internal class AudioHandler
    {

        public Action<AudioSample> OnAudioAvailable;
        public Action<AudioStatistics> OnStatisticsAvailable;
        private WaveOutEvent player;
        private BufferedWaveProvider soundListenBuffer;
        private WaveInEvent waveIn;
        private WaveFormat format = new WaveFormat(16000, 16, 1);
        private VolumeSampleProvider volumeSampleProvider;
        private Queue<AudioSample> delayedSamples =  new Queue<AudioSample>();
        private ConcurrentProtoSerialiser serialiser = new ConcurrentProtoSerialiser();
        DateTime lastSampleTime = DateTime.Now;

        private JitterBuffer collector;
        public bool SendTwice = false;

        private int bufferLatency = 200;
        public int BufferLatency { get => bufferLatency; 
            set 
            {
                if(value<bufferLatency)
                    collector.DiscardSamples((int)soundListenBuffer.BufferDuration.TotalMilliseconds/20);
                // drop the amount of buffered duration
                bufferLatency = value; 
                collector.BufferLatency = value;
            }
        }
        private float gain=3;
        public float Gain { get => gain; set { volumeSampleProvider.Volume = value; gain = value; } }
        public TimeSpan BufferedDuration =>soundListenBuffer.BufferedDuration;

        public bool LoopbackAudio { get; internal set; }

        private MemoryStreamPool streamPool = new MemoryStreamPool();
        private ushort currentSqnNo;
        private int lastLostPackckageAmount = 0;
        public AudioHandler()
        {
            soundListenBuffer = new BufferedWaveProvider(format);
            soundListenBuffer.BufferLength = format.SampleRate;
            soundListenBuffer.DiscardOnBufferOverflow = true;

            volumeSampleProvider = new VolumeSampleProvider(soundListenBuffer.ToSampleProvider());
            volumeSampleProvider.Volume = Gain; // double the amplitude of every sample - may go above 0dB

            player = new WaveOutEvent();
            player.DesiredLatency = 60;
            player.Init(volumeSampleProvider);
            player.Volume = 1;

            
            collector = new JitterBuffer(BufferLatency);
            collector.OnSamplesCollected += ProcessBufferedAudio;

            waveIn = new WaveInEvent();
            waveIn.WaveFormat = format;
            waveIn.BufferMilliseconds = 20;
            waveIn.DataAvailable += MicAudioRecieved;

            Task.Run(async() =>
            {
                while (true)
                {
                    await Task.Delay(100);
                    OnStatisticsAvailable?.Invoke(GetStatisticalData());
                }

            });

        }

        public AudioStatistics GetStatisticalData()
        {
            var data = new AudioStatistics()
            {
                BufferedDuration = (int)soundListenBuffer.BufferedDuration.TotalMilliseconds,
                BufferSize = (int)soundListenBuffer.BufferDuration.TotalMilliseconds,
                TotalNumDroppedPAckages = collector.NumLostPackages,
                NumLostPackages = (collector.NumLostPackages - lastLostPackckageAmount)/10,

            };
            lastLostPackckageAmount = collector.NumLostPackages;
            return data;
        }

        public void ResetStatistics()
        {
            collector.NumLostPackages= 0;
        }

        private void MicAudioRecieved(object sender, WaveInEventArgs e)
        {
            try
            {
                var res = Encode(e.Buffer, 0, e.BytesRecorded);
                currentSqnNo++;
                AudioSample sample = new AudioSample()
                {
                    Timestamp = DateTime.Now,
                    SquenceNumber = currentSqnNo,
                    Data = res,
                };

                if (LoopbackAudio)
                    ProcessAudio(sample);

                OnAudioAvailable?.Invoke(sample);
                if (SendTwice )
                {
                    delayedSamples.Enqueue(sample);
                    if(delayedSamples.Count > 5)
                    {
                        var sampleOld = delayedSamples.Dequeue();
                        OnAudioAvailable?.Invoke(sampleOld);
                    }
                   
                }

                //debug jitter
                return;

                if (false && currentSqnNo % 20 != 0 && currentSqnNo % 19 != 0 && currentSqnNo % 18 != 0)
                    ProcessAudio(sample);
                else
                {
                    Task.Run(async () =>
                    {
                        Random r = new Random(DateTime.Now.Millisecond);
                        await Task.Delay(r.Next(0, 300));
                        ProcessAudio(sample);



                    });
                }


            }
            catch { }
        }


        public void ProcessAudio(AudioSample sample)
        {
            collector.AddSample(sample);
        }

        public void ProcessAudio(MessageEnvelope packedSample)
        {
            var sample = serialiser.UnpackEnvelopedMessage<AudioSample>(packedSample);
            ProcessAudio(sample);

        }

        

        private void ProcessBufferedAudio(byte[] soundBytes,int offset, int count)
        {
            MemoryStream DecodeStream = streamPool.RentStream();

            Decode(DecodeStream, soundBytes, offset, count);
            soundListenBuffer?.AddSamples(DecodeStream.GetBuffer(), 0, (int)DecodeStream.Position);
            //Console.WriteLine(soundListenBuffer.BufferDuration.TotalMilliseconds);
            //Console.WriteLine(soundListenBuffer.BufferedDuration.TotalMilliseconds);
            streamPool.ReturnStream(DecodeStream);

        }

        public void StartSpeakers()
        {
            if (player.PlaybackState == PlaybackState.Playing)
                return;
            
            player.Play();
        }

        public void StopSpreakers()
        {
            if (player.PlaybackState == PlaybackState.Playing)

                player.Stop();
        }

        public void StartMic()
        {
            waveIn.StartRecording();
        }

        #region Encode - Decode Mlaw

        public byte[] Encode(byte[] data, int offset, int length)
        {
           
            var encoded = new byte[length / 2];
            int outIndex = 0;
            for (int n = 0; n < length; n += 2)
            {
                encoded[outIndex++] = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(data, offset + n));
            }
            return encoded;
        }

        public void Encode(MemoryStream encodeInto,byte[] data, int offset, int length)
        {
            if (encodeInto.Capacity < length / 2)
                encodeInto.Capacity = length / 2;

            var encoded = encodeInto.GetBuffer();
            int outIndex = 0;
            for (int n = 0; n < length; n += 2)
            {
                encoded[outIndex++] = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(data, offset + n));
            }
            encodeInto.Position = length / 2;
        }

        public byte[] Decode(byte[] data, int offset, int length)
        {
            var decoded = new byte[length * 2];
            int outIndex = 0;
            for (int n = 0; n < length; n++)
            {
                short decodedSample = MuLawDecoder.MuLawToLinearSample(data[n + offset]);
                decoded[outIndex++] = (byte)(decodedSample & 0xFF);
                decoded[outIndex++] = (byte)(decodedSample >> 8);
            }
            return decoded;
        }
        public void Decode(MemoryStream decodeInto, byte[] data, int offset, int length)
        {
            if(decodeInto.Capacity< length*2)
                decodeInto.Capacity = length*2;

            var decoded = decodeInto.GetBuffer();
            int outIndex = 0;
            for (int n = 0; n < length; n++)
            {
                short decodedSample = MuLawDecoder.MuLawToLinearSample(data[n + offset]);

                decoded[outIndex++] = (byte)(decodedSample & 0xFF);
                decoded[outIndex++] = (byte)(decodedSample >> 8);
            }
            decodeInto.Position = length * 2;


        }

        #endregion
        internal void FlushBuffers()
        {
            collector.DiscardSamples(100);
        }
    }
}

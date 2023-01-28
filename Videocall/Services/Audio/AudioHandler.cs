using NAudio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NetworkLibrary.Components;
using NetworkLibrary.Utils;
using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows;

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
        private DateTime lastSampleTime = DateTime.Now;
        private SharerdMemoryStreamPool streamPool = new SharerdMemoryStreamPool();
        private ushort currentSqnNo;
        private int lastLostPackckageAmount = 0;

        private JitterBuffer jitterBuffer;
        public bool SendTwice = false;

        private int bufferLatency = 200;
        public int BufferLatency { get => bufferLatency; 
            set 
            {
                if(value<bufferLatency)
                    jitterBuffer.DiscardSamples((int)soundListenBuffer.BufferDuration.TotalMilliseconds/20);
                // drop the amount of buffered duration
                bufferLatency = value; 
                jitterBuffer.BufferLatency = value;
            }
        }
        private float gain=3;
        public float Gain { get => gain; set { volumeSampleProvider.Volume = value; gain = value; } }
        public TimeSpan BufferedDuration =>soundListenBuffer.BufferedDuration;

        public bool LoopbackAudio { get; internal set; }

      
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

            
            jitterBuffer = new JitterBuffer(BufferLatency);
            jitterBuffer.OnSamplesCollected += ProcessBufferedAudio;

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
                TotalNumDroppedPAckages = jitterBuffer.NumLostPackages,
                NumLostPackages = (jitterBuffer.NumLostPackages - lastLostPackckageAmount)/10,

            };
            lastLostPackckageAmount = jitterBuffer.NumLostPackages;
            return data;
        }

        public void ResetStatistics()
        {
            jitterBuffer.NumLostPackages= 0;
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
                #region Test/Debug
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
                #endregion

            }
            catch { }
        }


        public void ProcessAudio(AudioSample sample)
        {
            jitterBuffer.AddSample(sample);
        }

        public void ProcessAudio(MessageEnvelope packedSample)
        {
            var sample = serialiser.UnpackEnvelopedMessage<AudioSample>(packedSample);
            ProcessAudio(sample);

        }

        private void ProcessBufferedAudio(byte[] soundBytes,int offset, int count)
        {
            var DecodeStream = streamPool.RentStream();

            Decode(DecodeStream, soundBytes, offset, count);
            soundListenBuffer?.AddSamples(DecodeStream.GetBuffer(), 0, (int)DecodeStream.Position);
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
            try
            {
                waveIn.StartRecording();
            }
            catch { }
        }
        public void StopMic()
        {
            try
            {
                waveIn.StopRecording();
            }
            catch { }
        }

        #region Encode - Decode Mlaw

        private byte[] Encode(byte[] data, int offset, int length)
        {
           
            var encoded = new byte[length / 2];
            int outIndex = 0;
            for (int n = 0; n < length; n += 2)
            {
                encoded[outIndex++] = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(data, offset + n));
            }
            return encoded;
        }

        private void Encode(PooledMemoryStream encodeInto,byte[] data, int offset, int length)
        {
            if (encodeInto.Length < length / 2)
                encodeInto.SetLength( length / 2);

            var encoded = encodeInto.GetBuffer();
            int outIndex = 0;
            for (int n = 0; n < length; n += 2)
            {
                encoded[outIndex++] = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(data, offset + n));
            }
            encodeInto.Position = length / 2;
        }

        private byte[] Decode(byte[] data, int offset, int length)
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
        private void Decode(PooledMemoryStream decodeInto, byte[] data, int offset, int length)
        {
            if(decodeInto.Length < length*2)
                decodeInto.SetLength( length*2);

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
            jitterBuffer.DiscardSamples(100);
        }
    }
}

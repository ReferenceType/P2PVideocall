using NAudio.Codecs;
using NAudio.Extras;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NetworkLibrary;
using NetworkLibrary.Components;
using NetworkLibrary.Utils;
using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar;

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
        public int TotalNumDroppedPackages;
    }
    internal class AudioHandler
    {
        public Action<AudioSample> OnAudioAvailable;
        public Action<AudioSample> OnAudioAvailableDelayed;
        public Action<AudioStatistics> OnStatisticsAvailable;

        private WaveOutEvent player;
        private BufferedWaveProvider soundListenBuffer;
        private WaveInEvent waveIn;
        private WaveFormat format = new WaveFormat(48000, 16, 1);
        private VolumeSampleProvider volumeSampleProvider;
        private Queue<AudioSample> delayedSamples =  new Queue<AudioSample>();
        private ConcurrentProtoSerialiser serialiser = new ConcurrentProtoSerialiser();
        private SharerdMemoryStreamPool streamPool = new SharerdMemoryStreamPool();
        private ushort currentSqnNo;
        private int lastLostPackckageAmount = 0;

        private JitterBuffer jitterBuffer;
        public bool SendMultiStream = true;

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

        private bool loopbackAudio;
        public bool LoopbackAudio { get => loopbackAudio; 
            set
            { 
                loopbackAudio = value;
                if (value)
                {
                    StartMic();
                }
                else /*if (!CallStateManager.IsOnACall)*/
                {
                    StopMic();
                }
            } 
        }
        private readonly int bitrate;
        private readonly G722CodecState encoderState;
        private readonly G722CodecState decoderState;
        private readonly G722Codec codec;
        bool Mlaw = false;
        public AudioHandler()
        {
            bitrate = 64000;
            encoderState = new G722CodecState(bitrate, G722Flags.None);
            decoderState = new G722CodecState(bitrate, G722Flags.None);
            codec = new G722Codec();

            soundListenBuffer = new BufferedWaveProvider(format);
            soundListenBuffer.BufferLength = 2*format.SampleRate;
            soundListenBuffer.DiscardOnBufferOverflow = true;

            volumeSampleProvider = new VolumeSampleProvider(soundListenBuffer.ToSampleProvider());
            volumeSampleProvider.Volume = Gain;

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
                TotalNumDroppedPackages = jitterBuffer.NumLostPackages,
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
                byte[] res;
                if(Mlaw)
                    res = EncodeMlaw(e.Buffer, 0, e.BytesRecorded);
                else
                    res = EncodeG722(e.Buffer, 0, e.BytesRecorded);
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
                if (SendMultiStream)
                {
                    delayedSamples.Enqueue(sample);
                    if(delayedSamples.Count > 1)
                    {
                        var sampleOld = delayedSamples.Dequeue();
                        OnAudioAvailableDelayed?.Invoke(sampleOld);
                    }
                   
                }
                //if(!Mlaw)
                //    BufferPool.ReturnBuffer(res);

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
            if(Mlaw)
                DecodeMlaw(DecodeStream, soundBytes, offset, count);
            else
                DecodeG722(DecodeStream, soundBytes, offset, count);

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

        #region Encode - Decode Mlaw - G722

       
     
        public byte[] EncodeG722(byte[] data, int offset, int length)
        {
            var buffer = BufferPool.RentBuffer(length);
            ByteCopy.BlockCopy(data, offset, buffer, 0, length);
            data = buffer;
           
            var wb = new WaveBuffer(data);
            int encodedLength = length / 4;
            var outputBuffer = new byte[encodedLength];
            int encoded = codec.Encode(encoderState, outputBuffer, wb.ShortBuffer, length / 2);
         
            BufferPool.ReturnBuffer(buffer);
            return outputBuffer;
        }

      
        public void DecodeG722(PooledMemoryStream decodeInto, byte[] data, int offset, int length)
        {
            var buffer = BufferPool.RentBuffer(length);
            ByteCopy.BlockCopy(data, offset, buffer,0,length);
            data= buffer;
          
            int decodedLength = length * 4;
            var outputBuffer = BufferPool.RentBuffer(decodedLength);// new byte[decodedLength];
            var wb = new WaveBuffer(outputBuffer);
            int decoded = codec.Decode(decoderState, wb.ShortBuffer, data, length);

            decodeInto.Write(outputBuffer,0,decodedLength);
            BufferPool.ReturnBuffer(buffer);
            BufferPool.ReturnBuffer(outputBuffer);
        }
        private byte[] EncodeMlaw(byte[] data, int offset, int length)
        {
            var encoded = new byte[length / 2];
            int outIndex = 0;
            for (int n = 0; n < length; n += 2)
            {
                encoded[outIndex++] = MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(data, offset + n));
            }
            return encoded;
        }

        private void DecodeMlaw(PooledMemoryStream decodeInto, byte[] data, int offset, int length)
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

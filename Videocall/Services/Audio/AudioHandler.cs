using NAudio.Codecs;
using NAudio.Extras;
using NAudio.Gui;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NetworkLibrary;
using NetworkLibrary.Components;
using NetworkLibrary.Utils;
using OpenCvSharp;
using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows;
using Videocall.UserControls;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar;

namespace Videocall
{
   
    class AudioSample
    {
        public DateTime Timestamp;

        public ushort SquenceNumber;
       
        public byte[] Data;

        public int DataLenght;
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
        public Action<AudioStatistics> OnStatisticsAvailable;
        public Action<SoundSliceData> OnSoundLevelAvailable;

        public bool LoopbackAudio
        {
            get => loopbackAudio;
            set
            {
                loopbackAudio = value;
                if (value)
                {
                    StartMic();
                }
                else if (!CallStateManager.IsOnACall)
                {
                    StopMic();
                }
            }
        }
        public int BufferLatency
        {
            get => bufferLatency;
            set
            {
                if (value < bufferLatency)
                    jitterBuffer.DiscardSamples((int)soundListenBuffer.BufferDuration.TotalMilliseconds / 20);
                // drop the amount of buffered duration
                bufferLatency = value;
                jitterBuffer.BufferLatency = value;
            }
        }
        public float Gain { get => gain; set { volumeSampleProvider.Volume = value; gain = value; } }
        public TimeSpan BufferedDuration => soundListenBuffer.BufferedDuration;

        public bool RectifySignal { get; internal set; }
        public bool EnableSoundVisualData { get; internal set; }

        public int BufferedDurationAvg = 200;
        public bool SendMultiStream = false;


        private WaveOutEvent player;
        private BufferedWaveProvider soundListenBuffer;
        private WaveInEvent waveIn;
        private JitterBuffer jitterBuffer;
        private readonly G722CodecState encoderState;
        private readonly G722CodecState decoderState;
        private readonly G722Codec codec;
        private readonly WaveFormat format = new WaveFormat(24000, 16, 1);
        private VolumeSampleProvider volumeSampleProvider;
        private Queue<AudioSample> delayedSamples =  new Queue<AudioSample>();
        private SharerdMemoryStreamPool streamPool = new SharerdMemoryStreamPool();
        private ushort currentSqnNo;
        private int lastLostPackckageAmount = 0;
        private int bufferLatency = 200;
        private int bitrate;
        private bool loopbackAudio;
        private float gain=1;
       
       
        public AudioHandler()
        {
            bitrate = 64000;
            encoderState = new G722CodecState(bitrate, G722Flags.None);
            decoderState = new G722CodecState(bitrate, G722Flags.None);
            codec = new G722Codec();

            soundListenBuffer = new BufferedWaveProvider(format);
            soundListenBuffer.BufferLength = format.SampleRate;
            soundListenBuffer.DiscardOnBufferOverflow = true;

            volumeSampleProvider = new VolumeSampleProvider(soundListenBuffer.ToSampleProvider());
            volumeSampleProvider.Volume = Gain;

            player = new WaveOutEvent();
            player.DesiredLatency = 60;
            player.Init(volumeSampleProvider);
            player.Volume = 1;

            jitterBuffer = new JitterBuffer(BufferLatency);
            jitterBuffer.OnSamplesCollected += DecodeAudio;

            waveIn = new WaveInEvent();
            waveIn.WaveFormat = format;
            waveIn.BufferMilliseconds = 20;
            waveIn.DataAvailable += MicrophoneSampleAvailable;

            Task.Run(async() =>
            {
                while (true)
                {
                    await Task.Delay(100);
                    OnStatisticsAvailable?.Invoke(GetStatisticalData());
                }
            });

        }

        private void MicrophoneSampleAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                byte[] res;
                res = EncodeG722(e.Buffer, 0, e.BytesRecorded, out int encoded);
                currentSqnNo++;
                AudioSample sample = new AudioSample()
                {
                    Timestamp = DateTime.Now,
                    SquenceNumber = currentSqnNo,
                    Data = res,
                    DataLenght = encoded,
                };

                if (EnableSoundVisualData)
                    CalculateAudioVisualData(e.Buffer, 0, e.BytesRecorded);

                if (LoopbackAudio)
                {
                    //Random r = new Random(DateTime.Now.Millisecond);
                    //Task.Delay(r.Next(0, 200)).ContinueWith((x) => ProcessAudio(sample));

                    // pass through jitter buff
                    //ProcessAudio(sample);

                    //directly to device
                    DecodeAudio(sample.Data, 0, sample.DataLenght);
                    
                    return;
                }

                OnAudioAvailable?.Invoke(sample);
                

                if (SendMultiStream)
                {
                    delayedSamples.Enqueue(sample);
                    if(delayedSamples.Count > 2)
                    {
                        var sampleOld = delayedSamples.Dequeue();
                        OnAudioAvailable?.Invoke(sampleOld);
                        BufferPool.ReturnBuffer(sampleOld.Data);
                    }
                   
                }
                else
                {
                    BufferPool.ReturnBuffer(sample.Data);
                }
               
            }
            catch { }
        }


        public void HandleRemoteAudioSample(AudioSample sample)
        {
            jitterBuffer.AddSample(sample);
            // jitter buffer will send them to decode.
        }

        private void DecodeAudio(byte[] soundBytes,int offset, int count)
        {
            var DecodeStream = streamPool.RentStream();
            DecodeG722(DecodeStream, soundBytes, offset, count);

            var buffer = DecodeStream.GetBuffer();
            int pos = DecodeStream.Position32;
            int offset_ = 0;
          
            soundListenBuffer?.AddSamples(buffer,offset_,pos);
            
            streamPool.ReturnStream(DecodeStream);

            var current = (int)soundListenBuffer.BufferedDuration.TotalMilliseconds+jitterBuffer.Duration;
            BufferedDurationAvg = (50 * BufferedDurationAvg + current) / 51;
        }

        float[] sums = new float[20];

        private void CalculateAudioVisualData(byte[] buffer, int offset_, int count)
        {

            for (int i = 0; i < 20; i++)
            {
                int c = count / 20;
                var sum = RectifySignal ? CalculateSliceSumRectified(buffer, offset_, c) : CalculateSliceSum(buffer, offset_, c);
                sums[i] = (3*sums[i]+sum )/4;
                offset_ += c;
            }
            SoundSliceData data = new SoundSliceData(sums);
            OnSoundLevelAvailable?.Invoke(data);
        }
       
        private float CalculateSliceSum(byte[] buffer, int offset, int count)
        {
            const int max = 196602/3;
            //if (max == 0)
            //{
            //    for (int i = 0; i < count / 2; i++)
            //    {
            //        max += short.MaxValue;
            //    }
            //    max = max / 4;
            //}
            float aLvl = 0;
            unsafe
            {
                fixed (byte* p = &buffer[offset])
                {
                    for (int i = 0; i < count; i += 2)
                    {
                        var sp = (short*)p;
                        short val = *sp;
                        aLvl += val;//(val + (val >> 31)) ^ (val >> 31);
                        sp++;
                    }
                }
            }
            return ((aLvl / max) * 50)+50;
        }
        private float CalculateSliceSumRectified(byte[] buffer, int offset, int count)
        {
            const int max = 196602 / 3;
           
            float aLvl = 0;
            unsafe
            {
                fixed (byte* p = &buffer[offset])
                {
                    for (int i = 0; i < count; i += 2)
                    {
                        var sp = (short*)p;
                        short val = *sp;
                        aLvl += (val + (val >> 31)) ^ (val >> 31);// abs val
                        sp++;
                    }
                }
            }
            return ((aLvl / max) * 100);
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
                currentSqnNo = 0;
            }
            catch { }
        }
        public void StopMic()
        {
            try
            {
                waveIn.StopRecording();
                currentSqnNo = 0;
            }
            catch { }
        }

        #region Encode - Decode Mlaw - G722

        public byte[] EncodeG722(byte[] data, int offset, int length, out int encoded)
        {
            var buffer = BufferPool.RentBuffer(length);
            ByteCopy.BlockCopy(data, offset, buffer, 0, length);
            data = buffer;
           
            var wb = new WaveBuffer(data);
            int encodedLength = length / 4;
            var outputBuffer = BufferPool.RentBuffer(encodedLength+50);//new byte[encodedLength];
            encoded = codec.Encode(encoderState, outputBuffer, wb.ShortBuffer, length / 2);
         
            BufferPool.ReturnBuffer(buffer);
            return outputBuffer;
        }

      
        public void DecodeG722(PooledMemoryStream decodeInto, byte[] data, int offset, int length)
        {
            // decoder doesnt support offsetted array..
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

        public AudioStatistics GetStatisticalData()
        {
            var data = new AudioStatistics()
            {
                BufferedDuration = (int)soundListenBuffer.BufferedDuration.TotalMilliseconds,
                BufferSize = (int)soundListenBuffer.BufferDuration.TotalMilliseconds,
                TotalNumDroppedPackages = jitterBuffer.NumLostPackages,
                NumLostPackages = (jitterBuffer.NumLostPackages - lastLostPackckageAmount) / 10,
            };
            lastLostPackckageAmount = jitterBuffer.NumLostPackages;
            return data;
        }

        public void ResetStatistics()
        {
            jitterBuffer.NumLostPackages = 0;
        }
    }
}

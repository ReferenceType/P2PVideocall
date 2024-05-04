using NetworkLibrary;
using NetworkLibrary.Components;
using NetworkLibrary.Utils;
using ServiceProvider.Services;
using ServiceProvider.Services.Audio;
using ServiceProvider.Services.Audio.Dependency;

namespace Videocall
{
    public class AudioHandler:IDisposable
    {
        public Action<AudioSample> OnAudioAvailable;
        public Action<AudioStatistics> OnStatisticsAvailable;
        public Action<SoundSliceData> OnSoundLevelAvailable;
        public Action InputDevicesUpdated;

        public bool LoopbackAudio
        {
            get => loopbackAudio;
            set
            {
                PrepLoopbackAudio(value);
                loopbackAudio = value;
            }
        }


        public int OutBufferCapacity {
            get => outBufferCapacity;
            set {
                outBufferCapacity = value;
                //ResizeOutputBuffer(value);
            }

        }

        public int JitterBufferMaxCapacity
        {
            get => jitterBufferMaxCapacity;
            set
            {
                if (value < jitterBufferMaxCapacity)
                    jitterBuffer.DiscardSamples((int)player.BufferDuration.TotalMilliseconds / captureInterval);
                // drop the amount of buffered duration
                jitterBufferMaxCapacity = value;
                jitterBuffer.BufferLatency = value;
               // OutBufferCapacity=value+100;
            }
        }

        public float Gain { get => gain;
            set 
            {
                player.Volume = value;
                gain = value;
            } 
        }

        public TimeSpan BufferedDuration => player.BufferedDuration;

        public bool RectifySignal { get; set; }
        public bool EnableSoundVisualData { get; set; }

        public int BufferedDurationAvg = 200;
        public bool SendMultiStream = false;
        public List<DeviceInfo> InputDevices = new List<DeviceInfo>();
        public DeviceInfo SelectedDevice = null;

        private IAudioOut player;
        private IAudioIn waveIn;
        private JitterBuffer jitterBuffer;
        private G722CodecState encoderState;
        private G722CodecState decoderState;
        private G722Codec codec;
        private Waveformat format = new Waveformat(24000, 16, 1);
        private Queue<AudioSample> delayedSamples =  new Queue<AudioSample>();
        private SharerdMemoryStreamPool streamPool = new SharerdMemoryStreamPool();
        private ushort currentSqnNo;
        private int lastLostPackckageAmount = 0;
        private int jitterBufferMaxCapacity = 200;
        private int captureInterval = 40;
        private int bitrate;
        private bool loopbackAudio;
        private float gain=1;
        private int disposing_ = 0;
        private int outBufferCapacity = 350;

        private readonly object operationLocker = new object();
        private readonly object commandLocker = new object();
        private readonly object networkLocker = new object();

        private bool useWasapi = true;
        
        private SingleThreadDispatcher marshaller;

        enum DeviceState
        {
            Uninitialized,
            Initialized,
            Playing,
            Paused,
            Stopped,
            Disposed
        }
       
        DeviceState outState;
        private DeviceState inState;
        public AudioHandler(IAudioIn input, IAudioOut player)
        {
            this.waveIn = input;
            this.player = player;
            marshaller = new SingleThreadDispatcher();
            EnumerateDevices();

            jitterBuffer = new JitterBuffer(JitterBufferMaxCapacity);
            jitterBuffer.OnSamplesCollected += DecodeAudio;
            jitterBuffer.CaptureInterval = captureInterval;

            bitrate = 64000;
            encoderState = new G722CodecState(bitrate, G722Flags.None);
            decoderState = new G722CodecState(bitrate, G722Flags.None);
            codec = new G722Codec();

           
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(100);

                    OnStatisticsAvailable?.Invoke(GetStatisticalData());
                }
            });
        }

       

        public void EnumerateDevices()
        {
            marshaller.EnqueueBlocking(() =>
            {
                EnumerateDevicesInternal();
            });
           
        }

        private void EnumerateDevicesInternal()
        {
            InputDevices = waveIn.EnumerateDevices();
            SelectedDevice= waveIn.SelectedDevice;
        }

        public void ResetDevices()
        {
            marshaller.Enqueue(() =>
            {
                bool playAgain= outState == DeviceState.Playing;
                bool captureAgain = inState == DeviceState.Playing;

                if(inState != DeviceState.Uninitialized)
                {
                    waveIn?.StopRecording();
                    waveIn?.Dispose();
                }
                if (outState != DeviceState.Uninitialized)
                {
                    player?.Stop();
                    player?.Dispose();
                }

                EnumerateDevicesInternal();

                outState = DeviceState.Uninitialized;
                InitOutputDevice();
                if(playAgain)
                    StartSpeakersInternal();

                inState = DeviceState.Uninitialized;
                InitInputDevice();
                if (captureAgain)
                    StartMicInternal();
            });

            

        }

        private void InitInputDevice()
        {
            if (inState != DeviceState.Uninitialized)
            {
                return;
            }
            waveIn.Init(format,20);
            waveIn.SampleAvailable += MicrophoneSampleAvailable;

            inState = DeviceState.Initialized;
        }

        private void InitOutputDevice()
        {
            if (outState != DeviceState.Uninitialized)
            {
                return;
            }
            player.Init(format);
            outState= DeviceState.Initialized;
        }

        private void UninitializeOutputDev()
        {
            if (outState != DeviceState.Uninitialized)
            {
                player?.Stop();
                player?.Dispose();
                outState= DeviceState.Uninitialized;
            }
        }
      
        private void MicrophoneSampleAvailable(byte[]buffer,int offset, int BytesRecorded)
        {
            try
            {
                captureInterval= 1000/(format.AverageBytesPerSecond/BytesRecorded);
                if(jitterBuffer.CaptureInterval!= captureInterval)
                    jitterBuffer.CaptureInterval = captureInterval;

                byte[] res;
                res = EncodeG722(buffer, offset, BytesRecorded, out int encoded);
                currentSqnNo++;
                AudioSample sample = new AudioSample()
                {
                    Timestamp = DateTime.Now,
                    SquenceNumber = currentSqnNo,
                    Data = res,
                    DataLenght = encoded,
                };

                if (EnableSoundVisualData)
                    CalculateAudioVisualData(buffer, 0, BytesRecorded);

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
            lock (networkLocker)
            {

                if (Interlocked.CompareExchange(ref disposing_, 0, 0) == 1)
                    return;
                if (outState != DeviceState.Playing)
                    return;
               
                jitterBuffer.AddSample(sample);
                // jitter buffer will send them to DecodeAudio.
            }
        }

        private void DecodeAudio(byte[] soundBytes,int offset, int count)
        {
            if (Interlocked.CompareExchange(ref disposing_, 0, 0) == 1)
                return;
            if (outState != DeviceState.Playing)
                return;


            var DecodeStream = streamPool.RentStream();
            DecodeG722(DecodeStream, soundBytes, offset, count);

            marshaller.Enqueue(() =>
            {
                if (Interlocked.CompareExchange(ref disposing_, 0, 0) == 1)
                    return;
                if (outState != DeviceState.Playing)
                    return;

                var buffer = DecodeStream.GetBuffer();
                int pos = DecodeStream.Position32;
                int offset_ = 0;

                player?.AddSamples(buffer, offset_, pos);
                streamPool.ReturnStream(DecodeStream);

                var current = (int)player.BufferedDuration.TotalMilliseconds + jitterBuffer.Duration;
                BufferedDurationAvg = (50 * BufferedDurationAvg + current) / 51;
            });


            
        }


        public void StartSpeakers()
        {
            lock (commandLocker)
            {
                marshaller.Enqueue(() =>
                {
                    StartSpeakersInternal();

                });

            }
        }
        private void StartSpeakersInternal()
        {
            if (outState == DeviceState.Playing)
                return;
            if (outState == DeviceState.Uninitialized)
                return;

            player.Play();
            outState = DeviceState.Playing;
        }
        public void StopSpreakers()
        {
            lock (commandLocker)
            {
                marshaller.Enqueue(() =>
                {
                    if (outState == DeviceState.Playing)
                    {
                        player.Stop();
                        outState= DeviceState.Stopped;
                    }
                });
            }
        }

        public void StartMic()
        {
            lock (commandLocker)
            {
                marshaller.Enqueue(() =>
                {
                    StartMicInternal();
                });

            }

        }
        private void StartMicInternal()
        {
            if (inState == DeviceState.Uninitialized)
                return;
            if (inState == DeviceState.Playing)
                return;

            try
            {
                currentSqnNo = 0;
                waveIn.StartRecording();
                inState = DeviceState.Playing;
            }
            catch { }
        }
        public void StopMic()
        {
            lock (commandLocker)
            {
                marshaller.Enqueue(() =>
                {
                    if (inState != DeviceState.Playing)
                        return;
                    try
                    {
                        waveIn?.StopRecording();
                        currentSqnNo = 0;
                        inState = DeviceState.Stopped;
                    }
                    catch { }
                });

            }

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
        

        #endregion

        internal void FlushBuffers()
        {
            jitterBuffer?.DiscardSamples(100);
        }

        public AudioStatistics GetStatisticalData()
        {
            var data = new AudioStatistics()
            {
                BufferedDuration = (int)player.BufferedDuration.TotalMilliseconds,
                BufferSize = (int)player.BufferDuration.TotalMilliseconds,
                TotalNumDroppedPackages = jitterBuffer.NumLostPackages,
                NumLostPackages = (jitterBuffer.NumLostPackages - lastLostPackckageAmount) / 10,
            };
            lastLostPackckageAmount = jitterBuffer.NumLostPackages;
            return data;
        }

        public void ResetStatistics()
        {
            if (jitterBuffer != null)
            {
                jitterBuffer.NumLostPackages = 0;
            }
        }

        #region Adudio visual data

        float[] sums = new float[20];
        private bool disposedValue;

        private void CalculateAudioVisualData(byte[] buffer, int offset_, int count)
        {

            for (int i = 0; i < 20; i++)
            {
                int c = count / 20;
                var sum = RectifySignal ? CalculateSliceSumRectified(buffer, offset_, c) : CalculateSliceSum(buffer, offset_, c);
                sums[i] = (3 * sums[i] + sum) / 4;
                offset_ += c;
            }
            SoundSliceData data = new SoundSliceData(sums);
            OnSoundLevelAvailable?.Invoke(data);
        }

        private float CalculateSliceSum(byte[] buffer, int offset, int count)
        {
            const int max = 196602 / 3;
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
            return ((aLvl / max) * 50) + 50;
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
        #endregion

        protected virtual void Dispose(bool disposing)
        {

            marshaller.EnqueueBlocking(() =>
            {
                Interlocked.Exchange(ref disposing_, 1);

                if (!disposedValue)
                  {
                      if (disposing)
                      {
                      }

                      try
                      {
                        player?.Stop();
                        outState = DeviceState.Stopped;

                        waveIn?.StopRecording();
                        inState = DeviceState.Stopped;

                        player?.Dispose();
                        waveIn?.Dispose();
                        outState = DeviceState.Uninitialized;
                        inState = DeviceState.Uninitialized;
                    }
                      catch { }
                      disposedValue = true;
                  }
                marshaller.Dispose();
            });
          
        }

        ~AudioHandler()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void InitializeDevices()
        {
            marshaller.EnqueueBlocking(() =>
            {
                if (inState == DeviceState.Uninitialized)
                    InitInputDevice();
                if (outState == DeviceState.Uninitialized)
                    InitOutputDevice();

            });
        }

        public void ShutdownDevices()
        {
            marshaller.Enqueue(() =>
            {
                player?.Stop();
                player?.Dispose();
                waveIn?.StopRecording();
                waveIn?.Dispose();

                outState = DeviceState.Uninitialized;
                inState = DeviceState.Uninitialized;
            });
        }
        private void PrepLoopbackAudio(bool on)
        {
            if (on)
            {
                InitializeDevices();
                StartSpeakers();
                StartMic();
            }
            else
            {
                ShutdownDevices();
            }
        }

        public void ChangeDevice()
        {
            bool playAgain= false;  
            marshaller.EnqueueBlocking(() =>
            {
                playAgain= inState == DeviceState.Playing;
                if (inState != DeviceState.Uninitialized)
                {
                    waveIn?.StopRecording();
                    waveIn?.Dispose();
                    inState = DeviceState.Uninitialized;
                }

                InitInputDevice();
                if(playAgain)
                {
                    StartMic();
                }
            });
           

        }
    }

    #region Small Data
    public class AudioSample
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
    #endregion

}

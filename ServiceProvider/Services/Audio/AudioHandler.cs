using NAudio.Codecs;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NetworkLibrary;
using NetworkLibrary.Components;
using NetworkLibrary.Utils;
using ServiceProvider.Services;
using ServiceProvider.Services.Audio;

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
                    jitterBuffer.DiscardSamples((int)soundListenBuffer.BufferDuration.TotalMilliseconds / captureInterval);
                // drop the amount of buffered duration
                jitterBufferMaxCapacity = value;
                jitterBuffer.BufferLatency = value;
               // OutBufferCapacity=value+100;
            }
        }
        public float Gain { get => gain;
            set 
            {
                volumeSampleProvider.Volume = value;
                gain = value;
            } 
        }
        public TimeSpan BufferedDuration => soundListenBuffer.BufferedDuration;

        public bool RectifySignal { get; set; }
        public bool EnableSoundVisualData { get; set; }

        public int BufferedDurationAvg = 200;
        public bool SendMultiStream = false;
        public List<DeviceInfo> InputDevices = new List<DeviceInfo>();
        public DeviceInfo SelectedDevice = null;

        private IWavePlayer player;
        private IWaveIn waveIn;

        private BufferedWaveProvider soundListenBuffer;
        private JitterBuffer jitterBuffer;
        private G722CodecState encoderState;
        private G722CodecState decoderState;
        private G722Codec codec;
        private WaveFormat format = new WaveFormat(24000, 16, 1);
        private VolumeSampleProvider volumeSampleProvider;
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
        public AudioHandler()
        {
            marshaller = new SingleThreadDispatcher();
            EnumerateDevices();

            jitterBuffer = new JitterBuffer(JitterBufferMaxCapacity);
            jitterBuffer.OnSamplesCollected += DecodeAudio;
            jitterBuffer.CaptureInterval = captureInterval;

            bitrate = 64000;
            encoderState = new G722CodecState(bitrate, G722Flags.None);
            decoderState = new G722CodecState(bitrate, G722Flags.None);
            codec = new G722Codec();

            soundListenBuffer = new BufferedWaveProvider(format);
            soundListenBuffer.BufferLength = 320 * format.AverageBytesPerSecond / 1000;
            soundListenBuffer.DiscardOnBufferOverflow = true;

            volumeSampleProvider = new VolumeSampleProvider(soundListenBuffer.ToSampleProvider());
            volumeSampleProvider.Volume = Gain;

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
            if (useWasapi)
            {
                InputDevices.Clear();
                var enumerator = new MMDeviceEnumerator();
                foreach (MMDevice wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
                {
                    InputDevices.Add(new DeviceInfo() { Name = wasapi.FriendlyName });
                }
                var inp = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                enumerator.Dispose();

                if (inp == null)
                    return;

                SelectedDevice= InputDevices.Where(x => x.Name == inp.FriendlyName).FirstOrDefault()!;
                InputDevicesUpdated?.Invoke();

            }
        }

        public void ResetDevices()
        {
            
            marshaller.Enqueue(() =>
            {
                bool playAgain= outState== DeviceState.Playing;
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

            if (useWasapi)
            {
                MMDevice inputDevice = null;

                var enumerator = new MMDeviceEnumerator();
                List<MMDevice> devices = new List<MMDevice>();
                foreach (MMDevice wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
                {
                    devices.Add(wasapi);
                }

                if (SelectedDevice != null)
                {
                    inputDevice = devices.Where(x => x.FriendlyName == SelectedDevice.Name).FirstOrDefault()!;

                }

                if (inputDevice == null)
                {
                    inputDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                }

                if (inputDevice == null)
                {
                    return;
                }
                var waveIn_ = new WasapiCapture(inputDevice, true, captureInterval);
                waveIn_.WaveFormat = format;
                waveIn_.DataAvailable += MicrophoneSampleAvailable;
                var old = Interlocked.Exchange(ref waveIn, waveIn_);
                //if (old != null && ((WasapiCapture)old).CaptureState == CaptureState.Capturing)
                //{
                //    old.StopRecording();
                //    old.Dispose();
                //    waveIn.StartRecording();
                //}
            }
            else
            {

                var waveIn_ = new WaveInEvent();
                waveIn_.BufferMilliseconds = captureInterval;
                waveIn_.WaveFormat = format;
                waveIn_.DataAvailable += MicrophoneSampleAvailable;

                var old = Interlocked.Exchange(ref waveIn, waveIn_);
                if (old != null)
                {
                    old.StopRecording();
                    old.Dispose();
                    waveIn.StartRecording();
                }
            }

            inState = DeviceState.Initialized;


        }

        private void InitOutputDevice()
        {
            if (outState != DeviceState.Uninitialized)
            {
                return;
            }
            if (useWasapi)
            {
                var outputDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
                if (outputDevice == null)
                    return;
                var player_ = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 60);
                player_.Init(volumeSampleProvider);
                Interlocked.Exchange(ref player, player_)?.Dispose();
            }
            else
            {
                var player_ = new WaveOutEvent();
                player_.DesiredLatency = 60;
                player_.Init(volumeSampleProvider);
                Interlocked.Exchange(ref player, player_)?.Dispose();

            }
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
        bool valueActive = false;
        bool operRunning = false;
        int lastVal= 0; 
        private async void ResizeOutputBuffer(int ms)
        {
            valueActive = true;
            lastVal = ms;

            if(operRunning)
                return;
            Top:
            operRunning = true;
            marshaller.Enqueue(() =>
            {
                bool replay = outState == DeviceState.Playing;

                soundListenBuffer.ClearBuffer();
                UninitializeOutputDev();
                soundListenBuffer = new BufferedWaveProvider(format);
                soundListenBuffer.BufferLength = lastVal * format.AverageBytesPerSecond / 1000;
                soundListenBuffer.DiscardOnBufferOverflow = true;
                InitOutputDevice();

               // if(replay)
                    StartSpeakers();

            });
            await Task.Delay(500);
            if (!valueActive)
                operRunning = false;
            else
            {
                valueActive = false;
               goto Top;
            }
           
        }
        private void MicrophoneSampleAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                captureInterval= 1000/(format.AverageBytesPerSecond/e.BytesRecorded);
                if(jitterBuffer.CaptureInterval!= captureInterval)
                    jitterBuffer.CaptureInterval = captureInterval;

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

                soundListenBuffer?.AddSamples(buffer, offset_, pos);
                streamPool.ReturnStream(DecodeStream);

                var current = (int)soundListenBuffer.BufferedDuration.TotalMilliseconds + jitterBuffer.Duration;
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
            jitterBuffer?.DiscardSamples(100);
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
    public class DeviceInfo
    {
        public string Name { get; set; }
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

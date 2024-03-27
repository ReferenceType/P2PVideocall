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
                if(value)
                    StartSpeakers();
                else 
                    StopSpreakers();
                loopbackAudio = value;
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
        private int bufferLatency = 200;
        private int bitrate;
        private bool loopbackAudio;
        private float gain=1;
        private int initialized = 0;
        private int disposing_ = 0;
        private int playerRunning = 0;
        private readonly object operationLocker = new object();
        private readonly object commandLocker = new object();
        private readonly object networkLocker = new object();
        public bool useWasapi = true;

        private SingleThreadDispatcher marshaller;

       
        public AudioHandler()
        {
            marshaller = new SingleThreadDispatcher();
            EnumerateDevices();
            jitterBuffer = new JitterBuffer(BufferLatency);
            jitterBuffer.OnSamplesCollected += DecodeAudio;
        }

       

        public void EnumerateDevices()
        {
            marshaller.EnqueueBlocking(() =>
            {
                if (useWasapi)
                {
                    InputDevices.Clear();
                    var enumerator = new MMDeviceEnumerator();
                    foreach (MMDevice wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    {
                        InputDevices.Add(new DeviceInfo() { Name = wasapi.FriendlyName });
                    }
                    InputDevicesUpdated?.Invoke();

                }
            });
           
        }
      
        public void CheckInit()
        {
            lock (operationLocker)
            {
                if (Interlocked.CompareExchange(ref initialized, 1, 0) == 0)
                {
                    marshaller.EnqueueBlocking(() =>
                    {
                        Init();
                    });
                  
                }
            }
        }
        private void Init()
        {
            bitrate = 64000;
            encoderState = new G722CodecState(bitrate, G722Flags.None);
            decoderState = new G722CodecState(bitrate, G722Flags.None);
            codec = new G722Codec();

            soundListenBuffer = new BufferedWaveProvider(format);
            soundListenBuffer.BufferLength = 65 * format.SampleRate / 100;
            soundListenBuffer.DiscardOnBufferOverflow = true;

            volumeSampleProvider = new VolumeSampleProvider(soundListenBuffer.ToSampleProvider());
            volumeSampleProvider.Volume = Gain;

            InitOutputDevice();

            

            InitInputDevice();

            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(100);

                    OnStatisticsAvailable?.Invoke(GetStatisticalData());
                }
            });
        }
        public void ResetDevices()
        {
            if (Interlocked.CompareExchange(ref initialized, 0, 0) == 1)
            {
                marshaller.Enqueue(() =>
                {
                    bool playAgain= player.PlaybackState == PlaybackState.Playing;
                    bool captureAgain= player.PlaybackState == PlaybackState.Playing;
                    player?.Stop();
                    player?.Dispose();
                    waveIn?.StopRecording();
                    waveIn?.Dispose();

                    EnumerateDevices();

                    InitOutputDevice();
                    if(playAgain)
                        StartSpeakers();

                    InitInputDevice();
                    if (captureAgain)
                        StartMic();
                });

            }

        }
        public void InitInputDevice()
        {
            marshaller.Enqueue(() =>
            {
                if (useWasapi)
                {
                    if (Interlocked.CompareExchange(ref initialized, 0, 0) == 0)
                        return;

                    MMDevice inputDevice = null;

                    var enumerator = new MMDeviceEnumerator();
                    List<MMDevice> devices = new List<MMDevice>();
                    foreach (MMDevice wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
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

                    var waveIn_ = new WasapiCapture(inputDevice, true, 20);
                    waveIn_.WaveFormat = format;
                    waveIn_.DataAvailable += MicrophoneSampleAvailable;

                    var old = Interlocked.Exchange(ref waveIn, waveIn_);
                    if (old != null && ((WasapiCapture)old).CaptureState == CaptureState.Capturing)
                    {
                        old.StopRecording();
                        old.Dispose();
                        waveIn.StartRecording();
                    }
                }
                else
                {
                    if (Interlocked.CompareExchange(ref initialized, 0, 0) == 0)
                        return;


                    var waveIn_ = new WaveInEvent();
                    waveIn_.BufferMilliseconds = 20;
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

            });            
        }

        private void InitOutputDevice()
        {
            if (useWasapi)
            {
                var outputDevice = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
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
            lock (networkLocker)
            {

                if (Interlocked.CompareExchange(ref disposing_, 0, 0) == 1)
                    return;
                if (Interlocked.CompareExchange(ref playerRunning, 0, 0) == 0)
                    return;
               
                jitterBuffer.AddSample(sample);
                // jitter buffer will send them to DecodeAudio.
            }
        }

        private void DecodeAudio(byte[] soundBytes,int offset, int count)
        {
            if (Interlocked.CompareExchange(ref disposing_, 0, 0) == 1)
                return;
            if (Interlocked.CompareExchange(ref playerRunning, 0, 0) == 0)
                return;


            var DecodeStream = streamPool.RentStream();
            DecodeG722(DecodeStream, soundBytes, offset, count);

            marshaller.Enqueue(() =>
            {
                if (Interlocked.CompareExchange(ref disposing_, 0, 0) == 1)
                    return;
                if (Interlocked.CompareExchange(ref playerRunning, 0, 0) == 0)
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

                CheckInit();
                marshaller.Enqueue(() =>
                {
                    if (player.PlaybackState == PlaybackState.Playing)
                        return;

                    player.Play();
                    Interlocked.Exchange(ref playerRunning, 1);
                });

            }
        }

        public void StopSpreakers()
        {
            lock (commandLocker)
            {
                marshaller.Enqueue(() =>
                {
                    if (player != null && player.PlaybackState == PlaybackState.Playing)
                    {
                        Interlocked.Exchange(ref playerRunning, 0);
                        player.Stop();
                    }
                });
            }
        }

        public void StartMic()
        {
            lock (commandLocker)
            {
                CheckInit();

                marshaller.Enqueue(() =>
                {

                    try
                    {
                        currentSqnNo = 0;
                        waveIn.StartRecording();
                    }
                    catch { }
                });

            }

        }
        public void StopMic()
        {
            lock (commandLocker)
            {
                marshaller.Enqueue(() =>
                {
                    try
                    {
                        waveIn?.StopRecording();
                        currentSqnNo = 0;
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
                Interlocked.Exchange(ref playerRunning, 0);

                if (!disposedValue)
                  {
                      if (disposing)
                      {
                      }

                      try
                      {
                          player?.Stop();
                          player?.Dispose();
                          waveIn?.StopRecording();
                          waveIn?.Dispose();
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

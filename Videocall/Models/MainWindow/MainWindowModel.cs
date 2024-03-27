using H264Sharp;
using NetworkLibrary;
using NetworkLibrary.Components;
using NetworkLibrary.P2P.Generic;
using NetworkLibrary.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
using ProtoBuf;
using ProtoBuf.Meta;
using ProtoBuf.Serializers;
using Protobuff;
using Protobuff.P2P;
using ServiceProvider.Services.Video;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Xml.Linq;
using Videocall;
using Videocall.Models;
using Videocall.Services.File_Transfer;
using Videocall.Services.Latency;
using Videocall.Settings;

internal class MainWindowModel
{

    private MainWindowViewModel mainWindowViewModel;
    private ServiceHub services;
    private ConcurrentDictionary<Guid, string> peers = new ConcurrentDictionary<Guid, string>();
    private FileTransferStateManager ftsm = new FileTransferStateManager();
    int busy = 0;
    private SoundPlayer hangupSound = new SoundPlayer(@"hangup2.wav");
    private SoundPlayer receivingCallSound = new SoundPlayer(@"ringtone.wav");
    private SoundPlayer callingSound = new SoundPlayer(@"marimba.wav");
    public MainWindowModel(MainWindowViewModel mainWindowViewModel, ServiceHub services)
    {
        this.mainWindowViewModel = mainWindowViewModel;
        this.services = services;

        services.VideoHandler.OnLocalImageAvailable += HandleCameraImage;
        services.VideoHandler.OnBytesAvailable += HandleCameraEncodedBytes;
        services.VideoHandler.OnBytesAvailableAction += HandleCameraEncodedBytes2;
        services.VideoHandler.OnRemoteImageAvailable += HandleNetworkFrame;
        services.VideoHandler.KeyFrameRequested += RequestKeyFrame;
        services.VideoHandler.LtrRecoveryRequest += RequestLtrRecover;
        services.VideoHandler.MarkingFeedback += MarkingFeedback;

        services.MessageHandler.OnMessageAvailable += HandleMessage;
        services.MessageHandler.OnPeerRegistered += HandlePeerRegistered;
        services.MessageHandler.OnPeerUnregistered += HandlePeerUnregistered;
        services.MessageHandler.OnDisconnected += () => ftsm.CancelAllStates();

        services.AudioHandler.OnAudioAvailable += HandleMicrophoneAudio;
        services.LatencyPublisher.Latency += LatencyDataAvailable;

        CallStateManager.Instance.StaticPropertyChanged += CallStateChanged;
        HandleScreenshare();

        SetupFileTransferControls();
        PreventSleep();
    }

    

    private void LatencyDataAvailable(object sender, LatencyEventArgs e)
    {
        foreach (var item in e.UdpLatency)
        {
            var info = mainWindowViewModel.PeerInfos.Where(x => x.Guid == item.Key).FirstOrDefault();
            if (info != null) info.UdpLatency = item.Value;
        }

        foreach (var item in e.TcpLatency)
        {
            var info = mainWindowViewModel.PeerInfos.Where(x => x.Guid == item.Key).FirstOrDefault();
            if (info != null) info.TcpLatency = item.Value;
        }

    }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
    [FlagsAttribute]
    public enum EXECUTION_STATE : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001
        // Legacy flag, should not be used.
        // ES_USER_PRESENT = 0x00000004
    }
    private void PreventSleep()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(30000);
                var currentstate = CallStateManager.GetState();
                if (currentstate == CallStateManager.CallState.OnCall)
                {
                    DispatcherRun(() => SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_AWAYMODE_REQUIRED |  EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_SYSTEM_REQUIRED));
                }
                else
                {
                    DispatcherRun(() => SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS));
                }
            }

        });
    }
    private void CallStateChanged(object sender, PropertyChangedEventArgs e)
    {

        var currState = CallStateManager.GetState();

        //if (currState == CallStateManager.CallState.ReceivingCall)
        //    Task.Delay(100).ContinueWith((t)=>receivingCallSound.PlayLooping());
        //else
        //    receivingCallSound.Stop();

        //if (currState == CallStateManager.CallState.Calling)
        //    callingSound.PlayLooping();
        //else
        //    callingSound.Stop();
        Task.Run(() =>
        {
            if (currState == CallStateManager.CallState.OnCall)
            {
                services.ResetBuffers();
                services.AudioHandler.CheckInit();
                services.AudioHandler.StartSpeakers();
                if (mainWindowViewModel.MicroponeChecked && currState == CallStateManager.CallState.OnCall)
                {
                    Task.Delay(50).ContinueWith((t) =>
                    {
                        services.AudioHandler.StartMic();
                    });
                }

                DispatcherRun(() =>
                {
                    HandleCamActivated(mainWindowViewModel.CameraChecked);

                    mainWindowViewModel.EndCallVisibility = true;
                    if (mainWindowViewModel.WindowWidth <= 1000)
                        mainWindowViewModel.WindowWidth = 1000;
                });


            }
            if (currState == CallStateManager.CallState.Available)
            {
               
                services.ResetBuffers();
                services.VideoHandler.HardReset();

                services.AudioHandler.StopSpreakers();
                services.AudioHandler.StopMic();
               // hangupSound.Play();

                HandleCamActivated(false);
                DispatcherRun(() =>
                {
                    services.VideoHandler.FlushBuffers();
                    mainWindowViewModel.EndCallVisibility = false;
                    mainWindowViewModel.ShareScreenChecked = false;
                    mainWindowViewModel.SecondaryCanvasSource = null;
                    mainWindowViewModel.PrimaryCanvasSource = null;
                });

                DispatcherRun(async () => {
                    await Task.Delay(500);
                    mainWindowViewModel.SecondaryCanvasSource = null;
                    mainWindowViewModel.PrimaryCanvasSource = null;
                });
            }
        });
      


       
}

    #region Message handling
    private void HandleMessage(MessageEnvelope message)
    {
        if (message.Header == null)
            return;
        switch (message.Header)
        {
            case MessageHeaders.Identify:
                SendId(message);
                break;
            case MessageHeaders.AudioSample:
                RemoteAudioSampleAvailable(message);
                break;
            case MessageHeaders.ImageMessage:
                HandleIncomingImage(message);
                break;
            case MessageHeaders.Text:
                HandleText(message);
                break;
            case MessageHeaders.FileDirectoryStructure:
                HandleFile(message);
                break;
            case MessageHeaders.Call:
                HandleRemoteCallRequest(message);
                break;
            case MessageHeaders.EndCall:
                HandleRemoteEndCall(message);
                break;
            case MessageHeaders.RemoteClosedCam:
                HandleRemoteCamClosed(message);
                break;
            case MessageHeaders.VideoAck:
                HandleVideoAck(message);
                break;
            case MessageHeaders.RequestKeyFrame:
                services.VideoHandler.ForceKeyFrame();
                break;

            case MessageHeaders.LtrRecoveryVH:
                DebugLogWindow.AppendLog("Info", "Ltr Recovery Req Receieved");
                services.VideoHandler.HandleLtrRecovery(message.Payload,message.PayloadOffset,message.PayloadCount);
                break;
            case MessageHeaders.MarkikngFeedbackVH:
                services.VideoHandler.HandleMarkingFeedback(message.Payload, message.PayloadOffset, message.PayloadCount);
                break;
            default:
                ftsm.HandleMessage(message);
                break;
        }
    }

    #endregion

    #region Registery

    private void HandlePeerUnregistered(Guid id)
    {
        DispatcherRun(() =>
        {
            peers.TryRemove(id, out var peerName);
            var info = mainWindowViewModel.PeerInfos.Where(x => x.Guid == id).FirstOrDefault();

            if (info != null)
                mainWindowViewModel.PeerInfos.Remove(info);

            CallStateManager.UnregisterCall(id);
        });
        ftsm.CancelAssociatedState(id);
    }

    private void HandlePeerRegistered(Guid peerId)
    {
        DebugLogWindow.AppendLog("Info", "Registery Received");
        Task.Run(async () =>
        {
            try
            {
                if (services.MessageHandler.registeredPeers.ContainsKey(peerId))
                {
                    MessageEnvelope env = new MessageEnvelope();
                    env.Header = MessageHeaders.Identify;
                    var response = await services.MessageHandler.SendRequestAndWaitResponse(peerId, env, 10000, RudpChannel.Realtime);

                    if (response.Header == MessageEnvelope.RequestTimeout)
                    {
                        DebugLogWindow.AppendLog("Error", "Registery timeout");
                        return;
                    }

                    if(response.KeyValuePairs == null || !response.KeyValuePairs.ContainsKey("Name"))
                    {
                        DebugLogWindow.AppendLog("Error", "Incompatible Peer");
                        return ;
                    }

                    string name = response.KeyValuePairs["Name"];
                    peers.TryAdd(peerId, name);

                    var info = services.MessageHandler.GetPeerInfo(peerId);
                    info.IP = IPAddress.Parse(info.IP).MapToIPv4().ToString();

                    DispatcherRun(() =>
                    {
                        var peerinfo = new VCPeerInfo(name, info.IP, info.Port, peerId);
                        mainWindowViewModel.PeerInfos.Add(peerinfo);

                        MainWindowEventAggregator.Instance.InvokePeerRegisteredEvent(peerinfo);

                        if (mainWindowViewModel.SelectedItem == null)
                            mainWindowViewModel.SelectedItem = mainWindowViewModel.PeerInfos.FirstOrDefault();

                        DebugLogWindow.AppendLog("Info", "Registered client ");

                    });

                }

            }
            catch (Exception ex)
            {
                DebugLogWindow.AppendLog("Error", ex.Message);
                services.MessageHandler.registeredPeers.TryRemove(peerId, out _);
            }

        });
    }
    private void SendId(MessageEnvelope message)
    {
        DebugLogWindow.AppendLog("Info", "Id Requested ");

        DispatcherRun(() =>
        {
            var response = new MessageEnvelope();
            response.MessageId = message.MessageId;
            response.Header = "ID";
            response.KeyValuePairs = new Dictionary<string, string>
                {
                    { "Name", PersistentSettingConfig.Instance.Name }
                };
            services.MessageHandler.SendAsyncMessage(message.From, response);
        });
    }
    #endregion

    #region File Transfer

    internal void SetupFileTransferControls()
    {
        mainWindowViewModel.OnFtCancelled += () => ftsm.CancelAllStates();

        ftsm.OnTransferCancelled = (state, s) =>
             DispatcherRun(async () =>
             {
                 await Task.Delay(200);
                 mainWindowViewModel.FTCancelBtnVisibility = false;
                 mainWindowViewModel.FTProgressText = MainWindowViewModel.FTProgressTextDefault;
                 mainWindowViewModel.FTProgressRatio = "";
                 mainWindowViewModel.FTProgressPercent = 0;
                 mainWindowViewModel.WriteInfoEntry("Send Cancelled : " + s.AdditionalInfo);
             });
        ftsm.OnReceiveCancelled = (state, s) =>
            DispatcherRun(async () =>
            {
                await Task.Delay(200);
                mainWindowViewModel.FTCancelBtnVisibility = false;
                mainWindowViewModel.FTProgressText = MainWindowViewModel.FTProgressTextDefault;
                mainWindowViewModel.FTProgressRatio = "";
                mainWindowViewModel.FTProgressPercent = 0;
                mainWindowViewModel.WriteInfoEntry("Receive Cancelled : " + s.AdditionalInfo);
            });
        ftsm.OnTransferStatus += (state, sts) => {
            if (Interlocked.CompareExchange(ref busy, 1, 0) == 0)
                DispatcherRun(async () =>
                {
                    mainWindowViewModel.FTCancelBtnVisibility = true;
                    mainWindowViewModel.FTProgressText = $"Sending %{sts.Percentage.ToString("N1")} {sts.FileName}";
                    mainWindowViewModel.FTProgressRatio = GetRatio(state);
                    mainWindowViewModel.FTProgressPercent = state.Progress;
                    await Task.Delay(40);
                    Interlocked.Exchange(ref busy, 0);

                });
        };
        ftsm.OnTransferComplete += (state, s) =>
        DispatcherRun(async () =>
        {
            await Task.Delay(200);
            mainWindowViewModel.FTCancelBtnVisibility = false;
            mainWindowViewModel.FTProgressText = MainWindowViewModel.FTProgressTextDefault;
            mainWindowViewModel.FTProgressRatio = "";
            mainWindowViewModel.FTProgressPercent = 0;
            mainWindowViewModel.WriteInfoEntry("Transfer Complete in " + s.elapsed.ToString(), s.Directory);
        });
        ftsm.OnReceiveStatus += (state, sts) =>
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) == 0)
                DispatcherRun(async () =>
                {
                    mainWindowViewModel.FTCancelBtnVisibility = true;
                    mainWindowViewModel.FTProgressText = $"Receiving %{sts.Percentage.ToString("N1")} {sts.FileName}";
                    mainWindowViewModel.FTProgressRatio = GetRatio(state);
                    mainWindowViewModel.FTProgressPercent = state.Progress;
                    await Task.Delay(40);
                    Interlocked.Exchange(ref busy, 0);

                });
        };
        ftsm.OnReceiveComplete += (state, s) =>
       DispatcherRun(async () =>
       {
           await Task.Delay(200);
           mainWindowViewModel.FTCancelBtnVisibility = false;
           mainWindowViewModel.FTProgressText = MainWindowViewModel.FTProgressTextDefault;
           mainWindowViewModel.FTProgressRatio = "";
           mainWindowViewModel.FTProgressPercent = 0;
           mainWindowViewModel.WriteInfoEntry("Received File in " + s.elapsed.ToString(),
               Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + @"\Shared" + s.Directory);
       });
        string GetRatio(IFileTransferState state)
        {
            return $"{BytesToString(state.TotalSize * state.Progress / 100)}/{BytesToString(state.TotalSize)}";
        }
    }
    static string[] dataSuffix = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };

    public static string BytesToString(long byteCount)
    {
        if (byteCount == 0)
            return "0" + dataSuffix[0];

        long bytes = Math.Abs(byteCount);
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        double num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return (Math.Sign(byteCount) * num).ToString() + " " + dataSuffix[place];
    }
    internal void HandleFileDrop(string[] files, Guid selectedPeer)
    {
        mainWindowViewModel.FTProgressText = "Preparing files";
        ftsm.SendFile(files, selectedPeer);
        return;
     
    }

    private void HandleFile(MessageEnvelope message)
    {
        if(message.Header == MessageHeaders.FileDirectoryStructure)
        {
            ftsm.HandleReceiveFile(message);
        }
        else
        {
            ftsm.HandleMessage(message);
        }
        return;
      
    }
    #endregion

    #region Text Message
    private void HandleText(MessageEnvelope message)
    {
        foreach (var item in message.KeyValuePairs)
        {
            mainWindowViewModel.WriteRemoteChat(item.Key, item.Value);
        }
    }

    internal void HandleUserChatSend(string chatInputText)
    {
        string textToSend = chatInputText;
        MessageEnvelope msg = new MessageEnvelope();
        msg.KeyValuePairs = new Dictionary<string, string>();
        msg.KeyValuePairs[PersistentSettingConfig.instance.Name] = textToSend;
        msg.Header = MessageHeaders.Text;

        if (mainWindowViewModel.SelectedItem != null)
        {
            var id = mainWindowViewModel.SelectedItem.Guid;
            services.MessageHandler.SendAsyncMessage(id, msg);
        }
    }

    #endregion

    #region Video
    internal void HandleCamActivated(bool camChecked)
    {
        var currentstate = CallStateManager.GetState();
      
        if (camChecked)
        {
            if(currentstate == CallStateManager.CallState.Calling)
               Task.Run(()=>services.VideoHandler.ObtainCamera());
            if (currentstate == CallStateManager.CallState.OnCall)
            {
                // call receiver shouldt acticate cam on call request, only when call is completed.

                Task.Run(() => services.VideoHandler.ObtainCamera()).ContinueWith((t)=>
                 services.VideoHandler.StartCapturing());
               
               
            }
        }
        else
        {
            if (currentstate == CallStateManager.CallState.OnCall)
            {
                MessageEnvelope msg = new MessageEnvelope();
                msg.Header = MessageHeaders.RemoteClosedCam;
                services.MessageHandler.SendAsyncMessage(CallStateManager.GetCallerId(), msg);
            }

            DispatcherRun(async () => { await Task.Delay(500); mainWindowViewModel.SecondaryCanvasSource = null; });
            services.VideoHandler.CloseCamera() ;
        }


    }
    int secondaryCanvasBusy = 0;
    private void HandleCameraImage(ImageReference image)
    {
        if(Interlocked.CompareExchange(ref secondaryCanvasBusy, 1, 0) == 1)
        {
            return;//throttle
        }
        DispatcherRun(() =>
        {
            try
            {
                if (image == null)
                {
                    mainWindowViewModel.SecondaryCanvasSource = null;
                }
                else if (mainWindowViewModel.SecondaryCanvasSource == null
                  || image.Width != mainWindowViewModel.SecondaryCanvasSource.Width
                  || image.Height != mainWindowViewModel.SecondaryCanvasSource.Height)
                {
                    mainWindowViewModel.SecondaryCanvasSource = new WriteableBitmap(image.Width,
                                                                                  image.Height,
                                                                                  96,
                                                                                  96,
                                                                                  PixelFormats.Bgr24,
                                                                                  null);
                }
                else
                {
                    var dst = (WriteableBitmap)mainWindowViewModel.SecondaryCanvasSource;
                    dst.Lock();
                    int width = image.Width;
                    int height = image.Height;
                    int step = image.Stride;
                    int range = width * height * 3;

                    dst.WritePixels(new Int32Rect(0, 0, width, height), image.DataStart, range, step);

                    dst.Unlock();
                    services.VideoHandler.ReturnImage(image);
                }
            }
            finally
            {
                Interlocked.Exchange(ref secondaryCanvasBusy, 0);
            }
        });

      
    }
    long idx = 0;
    private void HandleCameraEncodedBytes(byte[] imageBytes, int lenght, bool isKeyFrame)
    {
        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall &&
          mainWindowViewModel.CameraChecked)
        {

            MessageEnvelope env = new MessageEnvelope();
            env.TimeStamp = DateTime.Now;
            env.SetPayload(imageBytes, 0, lenght);

            env.Header = MessageHeaders.ImageMessage;
            env.MessageId = Guid.NewGuid();
            services.VideoHandler.ImageDispatched(env.MessageId, env.TimeStamp);

            bool isReliable = isKeyFrame && SettingsViewModel.Instance.Config.ReliableIDR;
            services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), env, reliable: isReliable);
        }
      
    }
    int cc = 0;
    private void HandleCameraEncodedBytes2(Action<PooledMemoryStream> action, int lenght, bool isKeyFrame)
    {
        //if (cc++ > 15&& !isKeyFrame)
        //{
        //    cc = 0;
        //    return;
        //}
        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall &&
          mainWindowViewModel.CameraChecked)
        {

            MessageEnvelope env = new MessageEnvelope();
            env.TimeStamp = DateTime.Now;
            env.Header = MessageHeaders.ImageMessage;
            env.MessageId = Guid.NewGuid();
            services.VideoHandler.ImageDispatched(env.MessageId, env.TimeStamp);

            bool isReliable = isKeyFrame /*&& SettingsViewModel.Instance.Config.ReliableIDR*/;
            services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), env, isReliable, action);
        }
    }
    private void HandleIncomingImage(MessageEnvelope message)
    {
        if (CallStateManager.GetState() != CallStateManager.CallState.OnCall)
            return;

        var img = message.Payload;
        services.VideoHandler.HandleIncomingImage(message.TimeStamp, message.Payload,message.PayloadOffset,message.PayloadCount);

        // ack + ts pong
        MessageEnvelope env = new MessageEnvelope();
        env.Header = MessageHeaders.VideoAck;
        env.MessageId = message.MessageId;
        services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), env,reliable:false);

        // video handler will invoke image ready event after some buffering
        // down there HandleNetworkFrame(Mat)
    }
    int primaryCanvasBusy = 0;
    

    private void HandleNetworkFrame(ImageReference image)
    {
        if(Interlocked.CompareExchange(ref primaryCanvasBusy, 1, 0) == 1)
        {
            return;
        }
        DispatcherRun(() =>
        {
            try
            {

                if (image == null)
                {
                    mainWindowViewModel.PrimaryCanvasSource = null;
                    return;

                }
                else if (image.Width == 0 || image.Height == 0) 
                {
                    return; 
                } 
                else if (mainWindowViewModel.PrimaryCanvasSource == null
                        || image.Width != mainWindowViewModel.PrimaryCanvasSource.Width
                        || image.Height != mainWindowViewModel.PrimaryCanvasSource.Height)
                {

                    mainWindowViewModel.PrimaryCanvasSource = new WriteableBitmap(image.Width,
                                                                                  image.Height,
                                                                                  96,
                                                                                  96,
                                                                                  PixelFormats.Bgr24,
                                                                                  null); 
                }
                
                {
                    var dst = (WriteableBitmap)mainWindowViewModel.PrimaryCanvasSource;
                    dst.Lock();
                    int width = image.Width;
                    int height = image.Height;
                    int step = image.Stride;
                    int range = width * height * 3;

                    dst.WritePixels(new Int32Rect(0, 0, width, height), image.DataStart, range, step);

                    dst.Unlock();
                    services.VideoHandler.ReturnImage(image);
                   
                }
            }
            finally
            {
                Interlocked.Exchange(ref primaryCanvasBusy, 0);
            }
           
        });
       

    }
    private void RequestKeyFrame()
    {
        services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), new MessageEnvelope() { Header = MessageHeaders.RequestKeyFrame}, reliable: true);

    }
    private void RequestLtrRecover(byte[] arg1, int arg2, int arg3)
    {
        var msg = new MessageEnvelope() { Header = MessageHeaders.LtrRecoveryVH };
        msg.SetPayload(arg1, arg2, arg3);
        services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), msg, reliable: true);

    }
    private void MarkingFeedback(byte[] arg1, int arg2, int arg3)
    {
        var msg = new MessageEnvelope() { Header = MessageHeaders.MarkikngFeedbackVH };
        msg.SetPayload(arg1, arg2, arg3);
        services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), msg, reliable: true);
    }
    private void HandleVideoAck(MessageEnvelope message)
    {
        services.VideoHandler.HandleAck(message);
    }
    private void HandleRemoteCamClosed(MessageEnvelope message)
    {
        DispatcherRun(async () => { await Task.Delay(1000); mainWindowViewModel.PrimaryCanvasSource = null; });
    }

    #endregion

    #region Sound
    private void HandleMicrophoneAudio(AudioSample sample)
    {
        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall &&
            mainWindowViewModel.MicroponeChecked)
        {
            MessageEnvelope envelope = new MessageEnvelope();
            envelope.Header = MessageHeaders.AudioSample;
            envelope.TimeStamp =sample.Timestamp;

            PrimitiveEncoder.WriteFixedUint16(sample.Data, sample.DataLenght, sample.SquenceNumber);
            

            envelope.SetPayload(sample.Data, 0, sample.DataLenght+2);
            services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), envelope, reliable: false);
        }

    }
  
    private void RemoteAudioSampleAvailable(MessageEnvelope packedSample)
    {
        //var sample = serialiser.UnpackEnvelopedMessage<AudioSample>(packedSample);
        var buffer = packedSample.Payload;
        int offset = packedSample.PayloadOffset;
        int count = packedSample.PayloadCount;

        ushort seqNo = BitConverter.ToUInt16(buffer,(offset+count-2));
        packedSample.Payload = ByteCopy.ToArray(buffer,offset,count-2);
        
        AudioSample sample = new AudioSample()
        {
            Timestamp = packedSample.TimeStamp,
            SquenceNumber = seqNo,
            Data = packedSample.Payload,
            DataLenght = packedSample.PayloadCount
            
        };
       if(CallStateManager.GetState() == CallStateManager.CallState.OnCall)
            services.AudioHandler.HandleRemoteAudioSample(sample);

    }
    internal void HandleMicChecked(bool value)
    {
        if (value&& CallStateManager.GetState() == CallStateManager.CallState.OnCall)
        {
            services.MessageHandler.SendAsyncMessage(CallStateManager.GetCallerId(), new MessageEnvelope() { Header = MessageHeaders.MicClosed });
            services.AudioHandler.StartMic();
        }
        else
        {
            services.AudioHandler.StopMic();
        }
    }

    #endregion

    #region Call Handling

    internal async void HandleCallUserRequest(VCPeerInfo SelectedItem)
    {
        if (!CallStateManager.CanSendCall()) return;
        try
        {
            var selectedPeerInfo = SelectedItem;
            if (selectedPeerInfo != null)
            {
                CallStateManager.Calling();
                // todo MY INFO NOT SELECTED
                var myInfo = new VCPeerInfo(PersistentSettingConfig.Instance.Name,null,0, services.MessageHandler.SessionId); 
                var response = await services.MessageHandler.SendRequestAndWaitResponse(selectedPeerInfo.Guid, myInfo, MessageHeaders.Call, 10000);
                if (response.Header != MessageEnvelope.RequestTimeout)
                    HandleCallResponse(response, selectedPeerInfo);
                else
                {
                    CallStateManager.CallRejected();
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogWindow.AppendLog("Error", "UserCall Request encountered an error: " + ex.Message);
        }

    }

    private async void HandleRemoteCallRequest(MessageEnvelope message)
    {
        DebugLogWindow.AppendLog("Info", "GotCallRequest");
        if (!CallStateManager.CanReceiveCall()) return;

        CallStateManager.ReceivingCall();

        try
        {
            App.ShowMainWindow();

            var callerInfo = services.MessageHandler.Serializer.UnpackEnvelopedMessage<VCPeerInfo>(message);
            //var t1 = AsyncToastNotificationHandler.ShowCallNotification(info.Name);
            Task<string> t2;
            if (SettingsViewModel.Instance.Config.AutoAcceptCalls)
            {
                await Task.Delay(1000);
                t2 = Task.FromResult(AsyncToastNotificationHandler.CallAccepted);
            }
            else
            {
                t2 = AlertWindow.ShowCallDialog(callerInfo.Name);
            }

            var res = await Task.WhenAny(/*t1,*/ t2);
            var result = res.Result;
            if (result == AsyncToastNotificationHandler.NotificationTimeout)
            {
                AsyncToastNotificationHandler.ShowInfoNotification(callerInfo.Name + " Called you.");
            }

            message.Header = "CallResponse";
            message.KeyValuePairs = new Dictionary<string, string>
            {
                { "Result", result }
            };

            AlertWindow.CancelDialog();
            DebugLogWindow.AppendLog("Info", "Sending Result: " + result);

            services.MessageHandler.SendAsyncMessage(callerInfo.Guid, message);
            DebugLogWindow.AppendLog("Info", "Result sent!");

            var id = services.MessageHandler.SessionId;

            if (result == AsyncToastNotificationHandler.CallAccepted)
                CallStateManager.RegisterCall(callerInfo.Guid);
            else
                CallStateManager.EndCall();
        }
        catch (Exception ex)
        {
            DebugLogWindow.AppendLog("Error", "REmote call request handler encounterred an error: " + ex.Message);
        }


    }
    private void HandleCallResponse(MessageEnvelope message, VCPeerInfo info)
    {
        DebugLogWindow.AppendLog("You", "Call Response Received");

        var result = message.KeyValuePairs["Result"];
        DebugLogWindow.AppendLog("You", "Result read : " + result);

        switch (result)
        {
            case "accept":
                CallStateManager.RegisterCall(info.Guid);
                break;
            case "reject":
                CallStateManager.CallRejected();
                break;
            case "timeout":
                CallStateManager.CallRejected();
                break;
            default:
                CallStateManager.CallRejected();
                break;
        }
    }
    internal void HandleEndCallUserRequest()
    {

        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall)
        {
            MessageEnvelope msg = new MessageEnvelope();
            msg.Header = MessageHeaders.EndCall;
            services.MessageHandler.SendAsyncMessage(CallStateManager.GetCallerId(), msg);

            CallStateManager.EndCall();
        }
    }
    private void HandleRemoteEndCall(MessageEnvelope message)
    {
        CallStateManager.EndCall();

    }
    #endregion

    #region Screen Share Handling
    private void HandleScreenshare()
    {
        var ScreenShareHandler = ServiceHub.Instance.ScreenShareHandler;
        var MainWindowViewModel = mainWindowViewModel;
        var MessageHandler = ServiceHub.Instance.MessageHandler;
        ScreenShareHandler.LocalImageAvailable += (image) =>
        {
            if(Interlocked.CompareExchange(ref secondaryCanvasBusy,1,0)==1)
            {
                return;
            }
            DispatcherRun(() => 
            {
                try
                {
                    if (image == null)
                        MainWindowViewModel.SecondaryCanvasSource = null;
                    else if (image.Width == 0 || image.Height == 0) return;
                    else if (MainWindowViewModel.SecondaryCanvasSource == null
                      || image.Width != MainWindowViewModel.SecondaryCanvasSource.Width
                      || image.Height != MainWindowViewModel.SecondaryCanvasSource.Height)
                    {
                        MainWindowViewModel.SecondaryCanvasSource = new WriteableBitmap(image.Width, image.Height, 96, 96,
                                 PixelFormats.Bgr24, null);
                    }
                    else
                    {
                        var dst = (WriteableBitmap)MainWindowViewModel.SecondaryCanvasSource;
                        dst.Lock();
                        int width = image.Width;
                        int height = image.Height;
                        int step = image.Stride;
                        int range = width * height * 3;
                        dst.WritePixels(new Int32Rect(0, 0, width, height), image.DataStart, range, step);


                        dst.Unlock();
                        // image.Dispose();
                       // services.ScreenShareHandler.ReturnImage(image);

                    }
                }
                finally
                {
                    Interlocked.Exchange(ref secondaryCanvasBusy, 0);
                }
            });

        };
        ScreenShareHandler.OnBytesAvailable = (byte[] bytes, int count, bool isKeyFrame) =>
        {
            if (!CallStateManager.IsOnACall)
                return;
            var env = new MessageEnvelope();
            env.Header = "SCI";
            env.TimeStamp = DateTime.Now;
            env.SetPayload(bytes, 0, count);

            bool isReliable = isKeyFrame /*&& SettingsViewModel.Instance.Config.ReliableIDR*/;
            MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), env, isReliable);
        };
       
        ScreenShareHandler.DecodedFrameWithAction = (writeBufferAction, count, isKeyFrame) =>
        {
            //if (cc++ > 15 && !isKeyFrame)
            //{
            //    cc = 0;
            //    return;
            //}
            if (!CallStateManager.IsOnACall)
                return;
            var env = new MessageEnvelope();
            env.Header = "SCI";
            env.TimeStamp = DateTime.Now;
            bool isReliable = isKeyFrame /*&& SettingsViewModel.Instance.Config.ReliableIDR*/;

            MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), env, isReliable, stream => writeBufferAction.Invoke(stream));

        };
        ScreenShareHandler.KeyFrameRequested = () =>
        {
            var env = new MessageEnvelope();
            env.Header = "SCKFR";
            MessageHandler.SendAsyncMessage(CallStateManager.GetCallerId(), env);
            DebugLogWindow.AppendLog("Info", "ScreenShare Key frame requested");

        };

        ScreenShareHandler.LtrRecoveryRequest = (b,o,c) =>
        {
            var env = new MessageEnvelope();
            env.Header = MessageHeaders.LtrRecoverySC;
            env.SetPayload(b, o, c);
            MessageHandler.SendAsyncMessage(CallStateManager.GetCallerId(), env);
            DebugLogWindow.AppendLog("Info", "ScreenShare Ltr recovery requested");

        };

        ScreenShareHandler.MarkingFeedback = (b, o, c) =>
        {
            var env = new MessageEnvelope();
            env.Header = MessageHeaders.MarkikngFeedbackSC;
            env.SetPayload(b, o, c);
            MessageHandler.SendAsyncMessage(CallStateManager.GetCallerId(), env);
            DebugLogWindow.AppendLog("Info", "ScreenShare Marking fb");

        };

        MessageHandler.OnMessageAvailable += (message) =>
        {
            if (message.Header == "SCI")
            {
                ScreenShareHandler.HandleNetworkImageBytes(message.TimeStamp, message.Payload, message.PayloadOffset, message.PayloadCount);
            }
            else if (message.Header.Equals("SCKFR"))
            {
                ScreenShareHandler.ForceKeyFrame();
            }
            else if (message.Header.Equals(MessageHeaders.LtrRecoverySC))
            {
                ScreenShareHandler.HandleLtrRecovery(message.Payload, message.PayloadOffset, message.PayloadCount);
                DebugLogWindow.AppendLog("Info", "ScreenShare Ltr recovery sending");

            }
            else if (message.Header.Equals(MessageHeaders.MarkikngFeedbackSC))
            {
                ScreenShareHandler.HandleMarkingFeedback(message.Payload, message.PayloadOffset, message.PayloadCount);
            }
        };

        ScreenShareHandler.RemoteImageAvailable += (image) =>
        {
            if(Interlocked.CompareExchange(ref primaryCanvasBusy, 1, 0) == 1)
            {
                return;
            }
            DispatcherRun(() =>
            {
                try
                {
                    if (image == null)
                    {
                        MainWindowViewModel.PrimaryCanvasSource = null;
                        return;
                    }

                    if (image.Width == 0 || image.Height == 0)
                    {
                        return;
                    }
                    else if (MainWindowViewModel.PrimaryCanvasSource == null
                            || image.Width != MainWindowViewModel.PrimaryCanvasSource.Width
                            || image.Height != MainWindowViewModel.PrimaryCanvasSource.Height)
                    {

                        MainWindowViewModel.PrimaryCanvasSource = new WriteableBitmap(image.Width, image.Height, 96, 96,
                                 PixelFormats.Bgr24, null);
                    }

                    {
                        var dst = (WriteableBitmap)MainWindowViewModel.PrimaryCanvasSource;
                        dst.Lock();
                        int width = image.Width;
                        int height = image.Height;
                        int step = image.Stride;
                        int range = width * height * 3;

                        dst.WritePixels(new Int32Rect(0, 0, width, height), image.DataStart, range, step);

                        dst.Unlock();
                        services.ScreenShareHandler.ReturnImage(image);
                    }

                }
                finally
                {
                    Interlocked.Exchange(ref primaryCanvasBusy, 0);
                }
            });
        };
    }
    internal void HandleShareScreenChecked(bool value)
    {
        if (value)
            services.VideoHandler.Pause();
        else
            services.VideoHandler.Resume();


        if (value)
            services.ScreenShareHandler.StartCapture();
        else
        {
            services.ScreenShareHandler.StopCapture();

            MessageEnvelope msg = new MessageEnvelope();
            msg.Header = MessageHeaders.RemoteClosedCam;
            services.MessageHandler.SendAsyncMessage(CallStateManager.GetCallerId(), msg);
        }

    }
    #endregion

    private void DispatcherRun(Action todo, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        try
        {
            Application.Current?.Dispatcher?.BeginInvoke(todo,priority);

        }
        catch (Exception ex)
        {
            DebugLogWindow.AppendLog("Error Dispatcher", ex.Message);

        }
    }

    
}
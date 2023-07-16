using NetworkLibrary;
using NetworkLibrary.P2P.Generic;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using ProtoBuf;
using ProtoBuf.Serializers;
using Protobuff;
using Protobuff.P2P;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Videocall;
using Videocall.Models;
using Videocall.Services.Latency;
using Videocall.Settings;
using Windows.Media.Protection.PlayReady;
using Windows.UI.Text;
using PeerInfo = Videocall.PeerInfo;

internal class MainWindowModel
{

    private MainWindowViewModel mainWindowViewModel;
    private ServiceHub services;
    private ConcurrentDictionary<Guid, string> peers = new ConcurrentDictionary<Guid, string>();

    public MainWindowModel(MainWindowViewModel mainWindowViewModel, ServiceHub services)
    {
        this.mainWindowViewModel = mainWindowViewModel;
        this.services = services;

        services.VideoHandler.OnCameraImageAvailable += HandleCameraImage;
        services.VideoHandler.OnNetworkFrameAvailable += HandleNetworkFrame;

        services.MessageHandler.OnMessageAvailable += HandleMessage;
        services.MessageHandler.OnPeerRegistered += HandlePeerRegistered;
        services.MessageHandler.OnPeerUnregistered += HandlePeerUnregistered;

        services.AudioHandler.OnAudioAvailable += HandleMicrophoneAduio;
        services.AudioHandler.OnAudioAvailableDelayed += HandleMicrophoneAduioSecondStream;
        services.LatencyPublisher.Latency += LatencyDataAvailable;

        CallStateManager.StaticPropertyChanged += CallStateChanged;
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

    private void CallStateChanged(object sender, PropertyChangedEventArgs e)
    {
        var currentstate = CallStateManager.GetState();
        if (currentstate == CallStateManager.CallState.OnCall || currentstate == CallStateManager.CallState.Calling)
        {
            if (mainWindowViewModel.MicroponeChecked)
                services.AudioHandler.StartMic();
            DispatcherRun(() =>
            {
                HandleCamActivated(mainWindowViewModel.CameraChecked);

                mainWindowViewModel.EndCallVisibility = true;
                if (mainWindowViewModel.WindowWidth <= 1000)
                    mainWindowViewModel.WindowWidth = 1000;
            });
        }
        else
        {
            HandleCamActivated(false);
            DispatcherRun(() =>
            {
                mainWindowViewModel.EndCallVisibility = false;
                mainWindowViewModel.SecondaryCanvasSource = null;
                mainWindowViewModel.PrimaryCanvasSource = null;

            });

            DispatcherRun(async () => {
                await Task.Delay(500);
                mainWindowViewModel.SecondaryCanvasSource = null;
                mainWindowViewModel.PrimaryCanvasSource = null;
            });

        }
    }

    #region Message handling
    private void HandleMessage(MessageEnvelope message)
    {
        
        switch (message.Header)
        {
            case MessageHeaders.Identify:
                SendId(message);
                break;
            case MessageHeaders.AudioSample:
                HandleIncomingSound(message);
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
            case "FTComplete":
                HandleFile(message);
                break;
            case MessageHeaders.FileTransfer:
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
    }

    private void HandlePeerRegistered(Guid peerId)
    {
        DebugLogWindow.AppendLog("Info", "Registery Received");
        Task.Run(async () =>
        {
            try
            {
                while (services.MessageHandler.registeredPeers.ContainsKey(peerId))
                {
                    MessageEnvelope env = new MessageEnvelope();
                    env.Header = MessageHeaders.Identify;
                    var response = await services.MessageHandler.SendRequestAndWaitResponse(peerId, env, 10000,RudpChannel.Realtime);
                    if (response.Header != MessageEnvelope.RequestTimeout)
                    {
                        if (response.KeyValuePairs == null)
                        {
                            services.MessageHandler.registeredPeers.TryRemove(peerId,out _);
                            return;
                        }

                        string name = response.KeyValuePairs[peerId.ToString()];
                        peers.TryAdd(peerId, name);
                        var info = services.MessageHandler.GetPeerInfo(peerId);
                        info.IP = IPAddress.Parse(info.IP).MapToIPv4().ToString();
                        DispatcherRun(() =>
                        {
                            var peerinfo = new Videocall.PeerInfo(name, info.IP, info.Port, peerId);
                            mainWindowViewModel.PeerInfos.Add(peerinfo);
                            MainWindowEventAggregator.Instance.InvokePeerRegisteredEvent(peerinfo);
                            if (mainWindowViewModel.SelectedItem == null)
                                mainWindowViewModel.SelectedItem = mainWindowViewModel.PeerInfos.FirstOrDefault();
                            DebugLogWindow.AppendLog("Info", "Registered client ");

                        });
                        break;
                    }
                    else
                    {
                        DebugLogWindow.AppendLog("Error", "Registery timeout");
                    }
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
                    { services.MessageHandler.SessionId.ToString(), PersistentSettingConfig.Instance.Name }
                };
            services.MessageHandler.SendAsyncMessage(message.From, response);
        });
    }
    #endregion

    #region File Transfer
    internal void HandleFileDrop(string[] files, Guid selectedPeer)
    {
        Task.Run(async () =>
        {
            FileDirectoryStructure tree = services.FileShare.CreateDirectoryTree(files[0]);
            services.MessageHandler.SendAsyncMessage(selectedPeer, new MessageEnvelope() { Header= "FileDirectoryStructure" }, tree);

            Stopwatch sw = new Stopwatch();
            DispatcherRun(() => mainWindowViewModel.FTProgressText = "Computing Hash..");

            List < FileTransfer> fileDatas = 
            services.FileShare.GetFiles(tree, chunkSize: int.Parse(PersistentSettingConfig.Instance.ChunkSize));

            sw.Start();
            try
            {
                Task prev = null;
                int windoowSize = Math.Max(1, (int)(20000000 / int.Parse(PersistentSettingConfig.Instance.ChunkSize)));
                int currentWindow = 0;
                MD5 md5 = new MD5CryptoServiceProvider();
                var dispatchedTasks = new List<Task>();

                for (int i = 0; i < fileDatas.Count; i++)
                {
                    var fileData = fileDatas[i];
                    fileData.ReadBytes();
                    // this one is to calculate hash during transfer progressively, i do it before so i commented this.
                    //if (fileData.IsLast)
                    //{
                    //    md5.TransformFinalBlock(fileData.Data, fileData.dataBufferOffset, fileData.count);
                    //    fileData.Hashcode = BitConverter.ToString(md5.Hash).Replace("-", "").ToLower();
                    //    md5.Dispose();
                    //    md5 = new MD5CryptoServiceProvider();
                    //}
                    //else
                    //{
                    //    md5.TransformBlock(fileData.Data, fileData.dataBufferOffset, fileData.count, fileData.Data, fileData.dataBufferOffset);
                    //}

                    var envelope = fileData.ConvertToMessageEnvelope(out byte[] chunkBuffer);

                    var res = services.MessageHandler.SendRequesAndWaitResponseFT(selectedPeer,
                          envelope,
                          timeout: 1200000, RudpChannel.Ch2);
#pragma warning disable CS4014 
                    res.ContinueWith((ignore,state) => 
                    {
                        UpdateDispatcher((FileTransfer)state);
                        ((FileTransfer)state).Release();
                    },fileData);
#pragma warning restore CS4014 

                    if (currentWindow == 0)
                    {
                        prev = res;
                    }
                    if (prev != null && currentWindow == windoowSize)
                    {
                        currentWindow = 0;
                        await prev;
                        prev = null;
                    }
                    else
                        currentWindow++;

                    dispatchedTasks.Add(res);
                }
                void UpdateDispatcher(FileTransfer fileData)
                {
                    DispatcherRun(() => 
                    {
                        if (fileData.IsLast)
                            mainWindowViewModel.FTProgressText = "";
                        else
                            mainWindowViewModel.FTProgressText = "%"
                        + (100 * ((float)fileData.SequenceNumber / (float)(fileData.TotalSequences))).ToString("N1")
                        + " Sending file: " + fileData.FilePath;

                    });
                }
                await Task.WhenAll(dispatchedTasks);
            }
            catch (Exception ex)
            {
                DebugLogWindow.AppendLog("Error", "Filetransfer drag drop encountered an error: " + ex.Message);
            }

            string name = "";
            var firstFolder = tree.FileStructure.Keys.First();

            if (!string.IsNullOrEmpty(firstFolder))
                name += firstFolder;
            else
                name += tree.FileStructure.Values.First().First();

            mainWindowViewModel.WriteInfoEntry("Transfer Complete in " + sw.Elapsed.ToString(), tree.seed + name);

            var response = new MessageEnvelope();
            response.Header = "FTComplete";
            response.KeyValuePairs = new Dictionary<string, string>() { { name, null } };
            services.MessageHandler.SendAsyncMessage(selectedPeer, response);


        });
    }

    private void HandleFile(MessageEnvelope message)
    {
        if (message.Header == MessageHeaders.FileTransfer)
        {
            var response = new MessageEnvelope();
            response.MessageId = message.MessageId;
            response.Header = "FileAck";

            var fileMsg = services.FileShare.HandleFileTransferMessage(message,out string error);

            if (error!=null)
                mainWindowViewModel.WriteInfoEntry("Error " +error);

            DispatcherRun(() =>
            {
                mainWindowViewModel.FTProgressText =
                "%" + (100 * ((float)fileMsg.SequenceNumber / (float)(fileMsg.TotalSequences))).ToString("N1") 
                + "Receiving file" + fileMsg.FilePath;
            });
            services.MessageHandler.SendAsyncMessage(message.From, response);


        }
        else if (message.Header == MessageHeaders.FileDirectoryStructure)
        {

            var fileTree = services.FileShare.HandleDirectoryStructure(message);
            int howManyFiles = fileTree.FileStructure.Values.Select(x => x.Count).Sum();
            DispatcherRun(() => mainWindowViewModel.FTProgressText = string.Format("Incoming {0} files", howManyFiles));
        }
        else
        {
            mainWindowViewModel.WriteInfoEntry("File Received",
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + @"\Shared" + message.KeyValuePairs.Keys.First());
            DispatcherRun(() => mainWindowViewModel.FTProgressText = "");
        }
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
    internal void HandleCamActivated(bool value)
    {
        var currentstate = CallStateManager.GetState();
       // services.VideoHandler.ObtainCamera();
        if (value && (currentstate == CallStateManager.CallState.OnCall || currentstate == CallStateManager.CallState.Calling))
        {
            services.VideoHandler.ObtainCamera();
            services.VideoHandler.StartCapturing();
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
            services.VideoHandler.CloseCamera();
        }


    }

    private void HandleCameraImage(byte[] imageBytes, Mat image)
    {
        DispatcherRun(() =>
        {
            if (mainWindowViewModel.SecondaryCanvasSource == null
              || image.Width != mainWindowViewModel.SecondaryCanvasSource.Width
              || image.Height != mainWindowViewModel.SecondaryCanvasSource.Height)
            {
                mainWindowViewModel.SecondaryCanvasSource = image.ToBitmapSource();
            }
            else
            {
                var dst = (WriteableBitmap)mainWindowViewModel.SecondaryCanvasSource;
                dst.Lock();
                int width = image.Width;
                int height = image.Height;
                int step = (int)image.Step();
                long range = image.DataEnd.ToInt64() - image.Data.ToInt64();

                dst.WritePixels(new Int32Rect(0, 0, width, height), image.Data, (int)range, step);
                dst.Unlock();
            }

        });

        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall &&
            mainWindowViewModel.CameraChecked)
        {

            MessageEnvelope env = new MessageEnvelope();
            env.TimeStamp = DateTime.Now;
            env.Payload = imageBytes;
            env.Header = MessageHeaders.ImageMessage;
            env.MessageId = Guid.NewGuid();
            services.VideoHandler.ImageDispatched(env.MessageId, env.TimeStamp);

            services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), env, reliable: SettingsViewModel.Instance.SendReliable);

        }
    }
    private void HandleIncomingImage(MessageEnvelope message)
    {
        var img = message.Payload;
        services.VideoHandler.HandleIncomingImage(message.TimeStamp, message.Payload,message.PayloadOffset,message.PayloadCount);

        // ack + ts pong
        MessageEnvelope env = new MessageEnvelope();
        env.Header = MessageHeaders.VideoAck;
        env.MessageId = message.MessageId;
        services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), env,reliable:true);

        // video handler will invoke image ready event after some buffering
        // down there HandleNetworkFrame(Mat)
    }
    private void HandleNetworkFrame(Mat image)
    {
        DispatcherRun(() =>
        {
            if (mainWindowViewModel.PrimaryCanvasSource == null
            || image.Width != mainWindowViewModel.PrimaryCanvasSource.Width 
            || image.Height != mainWindowViewModel.PrimaryCanvasSource.Height)
                mainWindowViewModel.PrimaryCanvasSource = image.ToBitmapSource();
            else
            {       // you have to be mindfull of memory with images.
                    var dst = (WriteableBitmap)mainWindowViewModel.PrimaryCanvasSource;
                    dst.Lock();
                    int width = image.Width;
                    int height = image.Height;
                    int step = (int)image.Step();
                    long range = image.DataEnd.ToInt64() - image.Data.ToInt64();

                    dst.WritePixels(new Int32Rect(0, 0, width, height), image.Data, (int)range, step);
                    dst.Unlock();
                image.Dispose();
            }
        });
       
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
    private void HandleMicrophoneAduio(AudioSample sample)
    {
        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall &&
            mainWindowViewModel.MicroponeChecked)
            services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), sample, reliable: SettingsViewModel.Instance.SendReliable);

    }
    private void HandleMicrophoneAduioSecondStream(AudioSample sample)
    {
        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall &&
            mainWindowViewModel.MicroponeChecked)
            services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), sample, reliable: false);

    }

    private void HandleIncomingSound(MessageEnvelope message)
    {
        services.AudioHandler.ProcessAudio(message);

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

    internal async void HandleCallUserRequest(PeerInfo SelectedItem)
    {
        if (!CallStateManager.CanSendCall()) return;
        try
        {
            var info = SelectedItem;
            if (info != null)
            {
                CallStateManager.Calling();
                // todo MY INFO NOT SELECTED
                var nfo = new PeerInfo(PersistentSettingConfig.Instance.Name,null,0, new Guid()); 
                var response = await services.MessageHandler.SendRequestAndWaitResponse(info.Guid, nfo, MessageHeaders.Call, 10000);
                if (response.Header != MessageEnvelope.RequestTimeout)
                    HandleCallResponse(response, info);
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

            var info = services.MessageHandler.Serializer.UnpackEnvelopedMessage<PeerInfo>(message);
            //var t1 = AsyncToastNotificationHandler.ShowCallNotification(info.Name);
            var t2 = AlertWindow.ShowCallDialog(info.Name);

            var res = await Task.WhenAny(/*t1,*/ t2);
            var result = res.Result;
            if (result == AsyncToastNotificationHandler.NotificationTimeout)
            {
                AsyncToastNotificationHandler.ShowInfoNotification(info.Name + " Called you.");
            }

            message.Header = "CallResponse";
            message.KeyValuePairs = new Dictionary<string, string>
            {
                { "Result", result }
            };
            AlertWindow.CancelDialog();
            DebugLogWindow.AppendLog("Info", "Sending Result: " + result);

            services.MessageHandler.SendAsyncMessage(message.From, message);
            DebugLogWindow.AppendLog("Info", "Result sent!");
            if (result == AsyncToastNotificationHandler.CallAccepted)
                CallStateManager.RegisterCall(message.To);
            else
                CallStateManager.EndCall();
        }
        catch (Exception ex)
        {
            DebugLogWindow.AppendLog("Error", "REmote call request handler encounterred an error: " + ex.Message);
        }


    }
    private void HandleCallResponse(MessageEnvelope message, PeerInfo info)
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
            CallStateManager.EndCall();

            MessageEnvelope msg = new MessageEnvelope();
            msg.Header = MessageHeaders.EndCall;
            services.MessageHandler.SendAsyncMessage(CallStateManager.GetCallerId(), msg);
        }
    }
    private void HandleRemoteEndCall(MessageEnvelope message)
    {
        CallStateManager.EndCall();

    }
    #endregion
    private void DispatcherRun(Action todo)
    {
        try
        {
            Application.Current?.Dispatcher?.BeginInvoke(todo);

        }
        catch (Exception ex)
        {
            DebugLogWindow.AppendLog("Error Dispatcher", ex.Message);

        }
    }

    internal void HandleShareScreenChecked(bool value)
    {
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
}
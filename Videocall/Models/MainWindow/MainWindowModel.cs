using OpenCvSharp;
using OpenCvSharp.Extensions;
using ProtoBuf;
using ProtoBuf.Serializers;
using Protobuff;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Videocall;
using Videocall.Models;
using Videocall.Services.Latency;
using Windows.Media.Protection.PlayReady;


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
        services.MessageHandler.client.OnPeerRegistered += HandlePeerRegistered;
        services.MessageHandler.client.OnPeerUnregistered += HandlePeerUnregistered;

        services.AudioHandler.OnAudioAvailable += HandleMicrophoneAduio;
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
            //mainWindowViewModel.CanvasColumn
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
        if (message.Header == MessageHeaders.FileTransfer)
        {
            //message.LockBytes();
            HandleFile(message);
            return;
        }
        message.LockBytes();
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
            //case MessageHeaders.FileTransfer:
            //    HandleFile(message);
            //break;
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
                while (services.MessageHandler.registeredPeers.Contains(peerId))
                {
                    MessageEnvelope env = new MessageEnvelope();
                    env.Header = MessageHeaders.Identify;
                    var response = await services.MessageHandler.client.SendRequestAndWaitResponse(peerId, env, 10000);
                    if (response.Header != MessageEnvelope.RequestTimeout)
                    {
                        if (response.KeyValuePairs == null)
                        {
                            services.MessageHandler.registeredPeers.Remove(peerId);
                            return;
                        }

                        string name = response.KeyValuePairs[peerId.ToString()];
                        peers.TryAdd(peerId, name);
                        var info = services.MessageHandler.client.GetPeerInfo(peerId);
                        info.IP = IPAddress.Parse(info.IP).MapToIPv4().ToString();
                        DispatcherRun(() =>
                        {
                            mainWindowViewModel.PeerInfos.Add(new Videocall.PeerInfo(name, info.IP, info.Port, peerId));
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
                services.MessageHandler.registeredPeers.Remove(peerId);

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
                    { services.MessageHandler.client.sessionId.ToString(), SettingConfig.Instance.Name }
                };
            services.MessageHandler.client.SendAsyncMessage(message.From, response);
        });
    }
    #endregion

    #region File Transfer
    internal void HandleFileDrop(string[] files, Guid selectedPeer)
    {
        FileDirectoryStructure tree = services.FileShare.CreateDirectoryTree(files[0]);

        services.MessageHandler.client.SendAsyncMessage(selectedPeer, tree);
        Stopwatch sw = new Stopwatch();
        List<FileTransfer> fileDatas = services.FileShare.GetFiles(tree, chunkSize: int.Parse(SettingConfig.Instance.ChunkSize));
        Task.Run(async () =>
        {
            sw.Start();

            try
            {
                Task prev = null;
                FileTransfer prevTaskData = null;

                for (int i = 0; i < fileDatas.Count; i++)
                {
                    var fileData = fileDatas[i];
                    DispatcherRun(() => mainWindowViewModel.FTProgressText =
                    "%" + (100 * ((float)fileData.SequenceNumber / (float)(1 + fileData.TotalSequences))).ToString("N1") + " Sending file: " + fileData.FilePath);

                    fileData.ReadBytes();
                    var envelope = fileData.ConvertToMessageEnvelope(out byte[] chunkBuffer);

                    var res = services.MessageHandler.client.SendRequestAndWaitResponse(selectedPeer,
                          envelope,
                          chunkBuffer,
                          0,
                          fileData.count,
                          timeoutMs: Math.Max(10000, fileData.count / 100));
                    res.GetAwaiter().OnCompleted(() => fileData?.Release());


                    if (prev != null)
                    {
                        await prev;
                        prev = null;
                    }
                    else 
                    {
                        prev = res;
                    }
                   
                    if (fileData.IsLast)
                    {
                       // mainWindowViewModel.WriteInfoEntry("Sent file: " + fileData.FilePath + " in " + sw.Elapsed.ToString());
                        //sw.Restart();
                    }

                }
                DispatcherRun(() => mainWindowViewModel.FTProgressText = "");
            }
            catch (Exception ex)
            {
                DebugLogWindow.AppendLog("Error", "Filetransfer drag drop encountered an error: " + ex.Message);
            }
            string name = "";
            var firstFolder = tree.FileStructure.Keys.First();
            if (!string.IsNullOrEmpty(firstFolder))
            {
                name += firstFolder;
            }
            else
                name += tree.FileStructure.Values.First().First();

             mainWindowViewModel.WriteInfoEntry("Transfer Complete: " + tree.seed+name + " in " + sw.Elapsed.ToString());

            var response = new MessageEnvelope();
            response.Header = "FTComplete";
            response.KeyValuePairs = new Dictionary<string, string>() { { name, null } };
            services.MessageHandler.client.SendAsyncMessage(selectedPeer, response);


        });
    }

    private void HandleFile(MessageEnvelope message)
    {
        if (message.Header == MessageHeaders.FileTransfer)
        {
            var response = new MessageEnvelope();
            response.MessageId = message.MessageId;
            response.Header = "FileAck";

            services.MessageHandler.client.SendAsyncMessage(message.From, response);
            var fileMsg = services.FileShare.HandleFileTransferMessage(message);

            DispatcherRun(() => {
                mainWindowViewModel.FTProgressText =
                "%" + (100 * ((float)fileMsg.SequenceNumber / (float)(1 + fileMsg.TotalSequences))).ToString("N1") + "Receiving file" + fileMsg.FilePath;
            });

            if (fileMsg.IsLast)
            {
               // mainWindowViewModel.WriteInfoEntry("Received a file " + fileMsg.FilePath);
                DispatcherRun(() => mainWindowViewModel.FTProgressText = "");
            }
        }
        else if(message.Header == MessageHeaders.FileDirectoryStructure)
        {

            var fileTree = services.FileShare.HandleDirectoryStructure(message);
            int howManyFiles = fileTree.FileStructure.Values.Select(x => x.Count).Sum();
            DispatcherRun(() => mainWindowViewModel.FTProgressText = string.Format("Incoming {0} files", howManyFiles));
        }
        else
        {
            mainWindowViewModel.WriteInfoEntry("Receive Complete " +
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop) +@"\Shared"+ message.KeyValuePairs.Keys.First());
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
        msg.KeyValuePairs[SettingConfig.instance.Name] = textToSend;
        msg.Header = MessageHeaders.Text;

        if (mainWindowViewModel.SelectedItem != null)
        {
            var id = mainWindowViewModel.SelectedItem.Guid;
            services.MessageHandler.client.SendAsyncMessage(id, msg);
        }


    }


    #endregion

    #region Video
    internal void HandleCamActivated(bool value)
    {
        var currentstate = CallStateManager.GetState();


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
                services.MessageHandler.client.SendAsyncMessage(CallStateManager.GetCallerId(), msg);
            }


            DispatcherRun(async () => { await Task.Delay(500); mainWindowViewModel.SecondaryCanvasSource = null; });
            services.VideoHandler.CloseCamera();
        }


    }

    private void HandleCameraImage(byte[] imageBytes, Mat arg2)
    {
        DispatcherRun(() =>
            mainWindowViewModel.SecondaryCanvasSource = arg2.ToBitmapSource());

        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall &&
            mainWindowViewModel.CameraChecked)
        {

            MessageEnvelope env = new MessageEnvelope();
            env.TimeStamp = DateTime.Now;
            env.Payload = imageBytes;
            env.Header = MessageHeaders.ImageMessage;
            env.MessageId = Guid.NewGuid();
            services.VideoHandler.ImageDispatched(env.MessageId, env.TimeStamp);
            services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), env);
        }
    }
    private void HandleIncomingImage(MessageEnvelope message)
    {
        var img = message.Payload;
        services.VideoHandler.HandleIncomingImage(message.TimeStamp, img);

        // ack + ts pong
        MessageEnvelope env = new MessageEnvelope();
        env.Header = MessageHeaders.VideoAck;
        env.MessageId = message.MessageId;
        services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), env);

        // video handler will invoke image ready event after some buffering
        // down there HandleNetworkFrame(Mat)
    }
    private void HandleNetworkFrame(Mat image)
    {
        DispatcherRun(() =>
            mainWindowViewModel.PrimaryCanvasSource = image.ToBitmapSource());

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
    private void HandleMicrophoneAduio(AudioSample obj)
    {
        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall &&
            mainWindowViewModel.MicroponeChecked)
            services.MessageHandler.SendStreamMessage(CallStateManager.GetCallerId(), obj);

    }

    private void HandleIncomingSound(MessageEnvelope message)
    {
        services.AudioHandler.ProcessAudio(message);

    }
    internal void HandleMicChecked(bool value)
    {
        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall)
            services.MessageHandler.client.SendAsyncMessage(CallStateManager.GetCallerId(), new MessageEnvelope() { Header = MessageHeaders.MicClosed });
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
                var response = await services.MessageHandler.client.SendRequestAndWaitResponse(info.Guid, info, MessageHeaders.Call, 10000);
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

            services.MessageHandler.client.SendAsyncMessage(message.From, message);
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
            services.MessageHandler.client.SendAsyncMessage(CallStateManager.GetCallerId(), msg);
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
            services.MessageHandler.client.SendAsyncMessage(CallStateManager.GetCallerId(), msg);
        }

    }
}
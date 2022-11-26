using OpenCvSharp;
using OpenCvSharp.Extensions;
using ProtoBuf;
using ProtoBuf.Serializers;
using Protobuff;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Videocall;
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
        if(currentstate == CallStateManager.CallState.OnCall ||currentstate == CallStateManager.CallState.Calling)
        {
            //mainWindowViewModel.CanvasColumn
            DispatcherRun(() =>
            {
                HandleCamActivated(mainWindowViewModel.CameraChecked);

                mainWindowViewModel.EndCallVisibility = true;
                if (mainWindowViewModel.WindowWidth <= 1000)
                    mainWindowViewModel.WindowWidth = 1000;

                mainWindowViewModel.ChatColumn = 280;
                mainWindowViewModel.CanvasColumn = -1;
                mainWindowViewModel.ChatColumn = 280;
                mainWindowViewModel.CanvasColumn = -1;

              
            });
        }
        else
        {
            HandleCamActivated(false);
            DispatcherRun(() =>
            {
                mainWindowViewModel.EndCallVisibility = false;
                mainWindowViewModel.SecondaryCanvasSource= null;
                mainWindowViewModel.PrimaryCanvasSource= null;
                mainWindowViewModel.CanvasColumn = 2;
                mainWindowViewModel.ChatColumn = -1;
                mainWindowViewModel.CanvasColumn = 2;
                mainWindowViewModel.ChatColumn = -1;
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
            case "Who":
                SendId(message);
                break;
            case "AudioSample":
                HandleIncomingSound(message);
                break;
            case "ImageMessage":
                HandleIncomingImage(message);
                break;
            case "Text":
                HandleText(message);
                break;
            case "FileDirectoryStructure":
                HandleFile(message);
                break;
            case "FileTransfer":
                HandleFile(message);
                break;
            case "Call":
                HandleRemoteCallRequest(message);
                break;
            case "EndCall":
                HandleRemoteEndCall(message);
                break;
            case "RemoteClosedCam":
                HandleRemoteCamClosed(message);
                break;
        }
    }

    private void HandleRemoteCamClosed(MessageEnvelope message)
    {
        DispatcherRun(async () => { await Task.Delay(500); mainWindowViewModel.PrimaryCanvasSource = null; }); 
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
                    env.Header = "Who";
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
                    { services.MessageHandler.client.sessionId.ToString(), SettingConfig.instance.Name }
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

        List<FileTransfer> fileDatas = services.FileShare.GetFiles(tree);
        Task.Run(async () =>
        {
            try
            {
                foreach (var item in fileDatas)
                {
                    DispatcherRun(() => mainWindowViewModel.FTProgressText = "Sending file: " + item.FilePath);
                    await services.MessageHandler.client.SendRequestAndWaitResponse(selectedPeer, item, null, Math.Min(10000,item.Data.Length/100));
                    mainWindowViewModel.WriteChatEntry("Info", "Sent file: " + item.FilePath);

                }
                DispatcherRun(() => mainWindowViewModel.FTProgressText = "");
            }
            catch(Exception ex)
            {
                DebugLogWindow.AppendLog("Error", "Filetransvfver drag drop encountered an error: " + ex.Message);
            }
            


        });
    }

    private void HandleFile(MessageEnvelope message)
    {
        if (message.Header == "FileTransfer")
        {
            var fileMsg = services.FileShare.HandleFileTransferMessage(message);
            DispatcherRun(() => { mainWindowViewModel.FTProgressText = string.Format("");});
            mainWindowViewModel.WriteChatEntry("You", "received a file " + fileMsg.FilePath);


            var response = new MessageEnvelope();
            response.MessageId = message.MessageId;
            response.Header = "FileAck";

            services.MessageHandler.client.SendAsyncMessage(message.From, response);

        }
        else
        {

            var fileTree = services.FileShare.HandleDirectoryStructure(message);
            int howManyFiles = fileTree.FileStructure.Values.Select(x => x.Count).Sum();
            DispatcherRun(() => mainWindowViewModel.FTProgressText = string.Format("Incoming {0} files", howManyFiles));
        }
    }
    #endregion

    #region Text Message
    private void HandleText(MessageEnvelope message)
    {
        foreach (var item in message.KeyValuePairs)
        {
             mainWindowViewModel.WriteChatEntry(item.Key, item.Value);
        }
    }
    internal void HandleUserChatSend(string chatInputText)
    {
        string textToSend = chatInputText;
        MessageEnvelope msg = new MessageEnvelope();
        msg.KeyValuePairs = new Dictionary<string, string>();
        msg.KeyValuePairs[SettingConfig.instance.Name] = textToSend;
        msg.Header = "Text";

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
            if(currentstate == CallStateManager.CallState.OnCall)
            {
                MessageEnvelope msg = new MessageEnvelope();
                msg.Header = "RemoteClosedCam";
                services.MessageHandler.client.SendAsyncMessage(CallStateManager.GetCallerId(), msg);
            }
          

            DispatcherRun(async () => { await Task.Delay(500); mainWindowViewModel.SecondaryCanvasSource = null;});
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
            ImageMessage im = new ImageMessage()
            {
                Frame = imageBytes,
                TimeStamp = DateTime.Now
            };

            services.MessageHandler.SendMessage(CallStateManager.GetCallerId(), im);
        }
    }
    private void HandleIncomingImage(MessageEnvelope message)
    {
        var img = services.MessageHandler.Serializer.UnpackEnvelopedMessage<ImageMessage>(message);
        services.VideoHandler.HandleIncomingImage(img);
        // video handler will invoke image ready event after some buffering
        // down there
    }
    private void HandleNetworkFrame(Mat obj)
    {
        DispatcherRun(() =>
            mainWindowViewModel.PrimaryCanvasSource = obj.ToBitmapSource());
    }
    #endregion

    #region Sound
    private void HandleMicrophoneAduio(AudioSample obj)
    {
        if (CallStateManager.GetState() == CallStateManager.CallState.OnCall &&
            mainWindowViewModel.MicroponeChecked)
            services.MessageHandler.SendMessage(CallStateManager.GetCallerId(), obj);

    }

    private void HandleIncomingSound(MessageEnvelope message)
    {
        services.AudioHandler.ProcessAudio(message);

    }
    internal void HandleMicChecked(bool value)
    {
        if(CallStateManager.GetState()== CallStateManager.CallState.OnCall)
            services.MessageHandler.client.SendAsyncMessage(CallStateManager.GetCallerId(),new MessageEnvelope() { Header = "MicClosed" });
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
                var response = await services.MessageHandler.client.SendRequestAndWaitResponse(info.Guid, info, "Call", 10000);
                if (response.Header != MessageEnvelope.RequestTimeout)
                    HandleCallResponse(response, info);
                else
                {
                    CallStateManager.CallRejected();
                }
            }
        }
        catch(Exception ex)
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
            var info = services.MessageHandler.Serializer.UnpackEnvelopedMessage<PeerInfo>(message);
            var t1 = AsyncToastNotificationHandler.ShowCallNotification(info.Name);
            var t2 = AlertWindow.ShowCallDialog(info.Name);

            var result = await Task.WhenAny(t1, t2).Result;

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
            msg.Header = "EndCall";
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

  
}



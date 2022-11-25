using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.Wave;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Protobuff;
using Protobuff.P2P;
using Windows.UI.Xaml.Media.Imaging;
using Path = System.IO.Path;

namespace Videocall
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window,INotifyPropertyChanged
    {
        private ImageSource BitmapSource_;
        public ImageSource BitmapSource { get=> BitmapSource_; set { OnPropertyChanged(); BitmapSource_ = value; } }
        private double clm;
        public double CanvasColumn { get => clm; set { OnPropertyChanged(); clm = value; } }
        private double cht = -1;
        public double ChatColumn { get => cht; set { OnPropertyChanged(); cht = value; } }
        protected virtual void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        RelayClient client;
        private  WaveOutEvent player;
        private  BufferedWaveProvider soundListenBuffer;
        bool IsConnected;
        bool playBackRunning;
        bool CamChecked;
        bool Soundchecked;
        private string soundQuality;
        private VideoCapture capture;
        private ConcurrentDictionary<Guid, string> peers = new ConcurrentDictionary<Guid, string>();
        public ObservableCollection<PeerInfo> PeerInfos { get; set; } = new ObservableCollection<PeerInfo>();
        public SettingConfig Config { get; set; } = SettingConfig.Instance;


        private HashSet<Guid> peerstoSend = new HashSet<Guid>();
        private CameraWindow cameraWindow =  new CameraWindow();

        AudioHandler audioHandler;
        VideoHandler videoHandler = new VideoHandler();

        ConcurrentProtoSerialiser serializer = new ConcurrentProtoSerialiser();
        private bool loopbackAudio;
        private string TransportLayer = "Udp";

        public event PropertyChangedEventHandler PropertyChanged;
        private FileShare FileShare = new FileShare();
        public MainWindow()
        {
            CallStateManager.StaticPropertyChanged += CallStateShanged;

            AsyncToastNotificationHandler.w = this;
            DataContext = this;

            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var cert = new X509Certificate2(path+"/client.pfx", "greenpass");
            client = new RelayClient(cert);

            client.OnMessageReceived += TcpMessageReceived;
            client.OnUdpMessageReceived += UdpMessageReceived ;

            audioHandler = new AudioHandler();
            audioHandler.OnAudioAvailable += OnAudioFromMicAvailable;

            audioHandler.StartMic();
            audioHandler.StartSpeakers();

            videoHandler.OnCameraImageAvailable += OnVideoImageAvailable;
            videoHandler.OnNetworkFrameAvailable += HandeFrame;

            client.OnPeerRegistered += PeerRegistered;
            client.OnPeerUnregistered += PeerUnRegistered;
            soundQuality = "Medium";
            
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_ProcessExit;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            Application.Current.Exit += Current_Exit;

            client.OnDisconnected += OnDisconnected;
            DispatcherRun(()=>Connect());
            InitializeComponent();
           
            Task.Run(async() => 
            {
                while (true)
                {

                    await Task.Delay(500);
                    var sts = client.GetUdpPingStatus();
                    foreach (var item in sts)
                    {
                        if (client.sessionId == item.Key)
                            DispatcherRun(() => ServerUdpLatency.Text = "Server Udp Latency: " + item.Value.ToString("N1") + " ms");
                        //else
                        //    DispatcherRun(()=>UdpLatency.Text = "Udp Latency: "+item.Value.ToString("N1") + " ms");

                        var info = PeerInfos.Where(x => x.Guid == item.Key).FirstOrDefault();
                        if(info!=null) info.UdpLatency = item.Value;
                    }

                    var sts2 = client.GetTcpPingStatus();
                    foreach (var item in sts2)
                    {
                        if (client.sessionId == item.Key)
                            DispatcherRun(() => ServerTcpLatency.Text = "Server Tcp Latency: " + item.Value.ToString("N1") + " ms");
                        //else
                        //    DispatcherRun(() => TcpLatency.Text = "Tcp Latency: " + item.Value.ToString("N1") + " ms");

                        var info = PeerInfos.Where(x => x.Guid == item.Key).FirstOrDefault();
                        if (info != null) info.TcpLatency = item.Value;

                    }
                }
               
            });

        }


        #region message handling
        private void UdpMessageReceived(MessageEnvelope message)
        {
            HandleMessage(message);
        }

        private void TcpMessageReceived(MessageEnvelope message)
        {
            HandleMessage(message);
        }

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
                    HandleCall(message);
                    break;
                case "EndCall":
                    HandleEndCall(message);
                    break;
            }
        }
        #endregion

        #region Video Handling
        private void OnVideoImageAvailable(byte[] imageBytes, Mat frame)
        {

            DispatcherRun(() =>
            {
                if (CamChecked)
                {
                  //  BitmapSource = frame.Flip(FlipMode.Y).ToBitmapSource();
                    // SecondaryVideoCanvas.Source = frame.Flip(FlipMode.Y).ToBitmapSource();
                    //MainVideoCanvas.Source = SecondaryVideoCanvas.Source;
                }
            });

            ImageMessage im = new ImageMessage()
            {
                Frame = imageBytes,
                TimeStamp = DateTime.Now
            };
            //var e = new MessageEnvelope();
            //e.Payload = imageBytes;
            //HandleIncomingImage(e);

            if (CallStateManager.GetState() == CallStateManager.CallState.OnCall)
            {
                if (TransportLayer == "Udp")
                    client.SendUdpMesssage(CallStateManager.GetCallerId(), im);

                else
                    client.SendAsyncMessage(CallStateManager.GetCallerId(), im);
            }

        }

        private void HandleIncomingImage(MessageEnvelope message)
        {
            var img = serializer.UnpackEnvelopedMessage<ImageMessage>(message);

            videoHandler.HandleIncomingImage(img);
            // videoHandler.HandleIncomingVideo(message.Payload);
        }
        private void HandeFrame(Mat frame)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                var src = frame.ToBitmapSource();

                MainVideoCanvas.Source = src;
                cameraWindow.Canvas.Source = src;
            }));



        }

        private void CameraButton_Checked(object sender, RoutedEventArgs e)
        {
            if ((sender as ToggleButton).IsChecked == true)
            {
                CamChecked = true;
                videoHandler.ObtainCamera();
                videoHandler.StartCapturing();



            }
            else
            {
                videoHandler.CloseCamera();
                CamChecked = false;
                SecondaryVideoCanvas.Source = null;




            }

        }

        #endregion

        #region Aduio Handling

        private void OnAudioFromMicAvailable(AudioSample sample)
        {
            if (loopbackAudio)
            {
                audioHandler.ProcessAudio(sample);
            }

            if (!Soundchecked)
                return;
            if (CallStateManager.GetState() == CallStateManager.CallState.OnCall)
            {
                if (TransportLayer == "Udp")
                    client.SendUdpMesssage(CallStateManager.GetCallerId(), sample);

                else
                    client.SendAsyncMessage(CallStateManager.GetCallerId(), sample);
            }
           
        }

        private void HandleIncomingSound(MessageEnvelope message)
        {
            audioHandler.ProcessAudio(message);
        }

        private void SoundQuality_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            soundQuality = (e.AddedItems[0] as ComboBoxItem).Content as string;
        }

        private void SoundButton_Checked(object sender, RoutedEventArgs e)
        {
            if ((sender as ToggleButton).IsChecked == true)
            {
                Soundchecked = true;
                return;
            }
            Soundchecked = false;

        }

        #endregion

        #region Call Handling


        private void HandleEndCall(MessageEnvelope message)
        {
            CallStateManager.EndCall();
        }

        private async void HandleCall(MessageEnvelope message)
        {
            WtireTextOnChatWindow("GotCallRequest");

            if (!CallStateManager.CanReceiveCall()) return;
            CallStateManager.ReceivingCall();

            var info = serializer.UnpackEnvelopedMessage<PeerInfo>(message);
            var result = await AsyncToastNotificationHandler.ShowCallNotification(info.Name);

            message.Header = "CallResponse";
            message.KeyValuePairs = new Dictionary<string, string>
            {
                { "Result", result }
            };
            WtireTextOnChatWindow("Sending Result: " +result);

            client.SendAsyncMessage(message.From, message);
                WtireTextOnChatWindow("Result sent!");
            if (result == AsyncToastNotificationHandler.CallAccepted)
                CallStateManager.RegisterCall(message.To);
            else
                CallStateManager.EndCall();
           

        }

        private void HandleCallResponse(MessageEnvelope message,PeerInfo info)
        {
            WtireTextOnChatWindow("Call Response Received");

            var result = message.KeyValuePairs["Result"];
            WtireTextOnChatWindow("Result read : "+result);

            Console.WriteLine(result);
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
        private async void CallButtonClick(object sender, RoutedEventArgs e)
        {
            if (!CallStateManager.CanSendCall()) return;

            var info = (PeerInfo)lvUsers.SelectedItem;
            if (info != null)
            {
                CallStateManager.Calling();
                var response = await client.SendRequestAndWaitResponse(info.Guid, info, "Call", 10000);
                if (response.Header != MessageEnvelope.RequestTimeout)
                    HandleCallResponse(response, info);
                else
                {
                    CallStateManager.CallRejected();
                }
            }

        }

        private void EndCallButtonClicked(object sender, RoutedEventArgs e)
        {
            if (CallStateManager.GetState() == CallStateManager.CallState.OnCall)
            {
                CallStateManager.EndCall();

                MessageEnvelope msg = new MessageEnvelope();
                msg.Header = "EndCall";
                client.SendAsyncMessage(CallStateManager.GetCallerId(), msg);
            }

        }
        private void CallStateShanged(object sender, PropertyChangedEventArgs e)
        {
            if (CallStateManager.CurrentState == "OnCall" || CallStateManager.CurrentState == "Calling")
                ExpandImageCanvas();
            else
            {
                CollapseImageCanvas();
            }
        }

        #endregion Call Handlling

        #region Chat Handling
        private void HandleText(MessageEnvelope message)
        {
            foreach (var item in message.KeyValuePairs)
            {
                DispatcherRun(() => { MessageWindow.Text += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + item.Key + " : " + item.Value; });

            }

        }

        public void WtireTextOnChatWindow(string text)
        {
            DispatcherRun(() =>
            {
                MessageWindow.Text += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + "You" + " : " + text;
                MessageWindow.ScrollToEnd();
            });
        }
        private void SendMessageButtonClicked(object sender, RoutedEventArgs e)
        {
            string textToSend = ChatMessageBox.Text;
            MessageEnvelope msg = new MessageEnvelope();
            msg.KeyValuePairs = new Dictionary<string, string>();
            msg.KeyValuePairs[UserName.Text] = textToSend;
            msg.Header = "Text";

            MessageWindow.Text += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + "You" + " : " + textToSend;
            MessageWindow.ScrollToEnd();

            foreach (var peer in peerstoSend)
            {
                client.SendAsyncMessage(peer, msg);

            }

            DispatcherRun(() => ChatMessageBox.Text = "");

        }

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter &&
                !(Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) &&
                 !(Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
            {
                SendMessageButtonClicked(null, null);
            }
        }
        #endregion

        #region PeerRegistration

        private void PeerRegistered(Guid peerId)
        {
            peerstoSend.Add(peerId);

            Task.Run(async () =>
            {
                MessageEnvelope env = new MessageEnvelope();
                env.Header = "Who";
                var response = await client.SendRequestAndWaitResponse(peerId, env, 3000);
                if (response.Header != MessageEnvelope.RequestTimeout)
                {
                    string name = response.KeyValuePairs[peerId.ToString()];
                    peers.TryAdd(peerId, name);
                    var info = client.GetPeerInfo(peerId);
                    info.IP = IPAddress.Parse(info.IP).MapToIPv4().ToString();
                    DispatcherRun(() => PeerInfos.Add(new PeerInfo(name, info.IP, info.Port, peerId)));
                }
            });
           
        }
        private void PeerUnRegistered(Guid id)
        {
            DispatcherRun(() =>
            {
                peerstoSend.Remove(id);
                peers.TryRemove(id, out var peerName);
                var info = PeerInfos.Where(x => x.Guid == id).FirstOrDefault();

                if (info != null)
                    PeerInfos.Remove(info);

                CallStateManager.UnregisterCall(id);
            });
           
        }
        private void SendId(MessageEnvelope message)
        {
            DispatcherRun(() =>
            {
                var response = new MessageEnvelope();
                response.MessageId = message.MessageId;
                response.Header = "ID";
                response.KeyValuePairs = new Dictionary<string, string>
                {
                    { client.sessionId.ToString(), UserName.Text }
                };
                client.SendAsyncMessage(message.From, response);
            });


        }
        #endregion

        #region exit 
        private void Current_Exit(object sender, ExitEventArgs e)
        {
            videoHandler.CloseCamera();

        }

        private void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Environment.Exit(0);
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            videoHandler.CloseCamera();
        }
        #endregion

        #region Settings
        private void ConnectButtonClick(object sender, RoutedEventArgs e)
        {
            Connect();

        }
        private async void Connect()
        {
            try
            {
                DispatcherRun(() => { IsConnected = false; Status.Text += "+\nConnecting.."; });
                await client.ConnectAsync(IpText.Text, int.Parse(PortText.Text));
                DispatcherRun(() => { IsConnected = false; Status.Text += "\nConnected"; });

            }
            catch
            {
                DispatcherRun(() => { IsConnected = false; Status.Text += "\nError.."; });

            }
        }

        private void ListenYourselfToggled(object sender, RoutedEventArgs e)
        {
            loopbackAudio = (bool)((CheckBox)sender).IsChecked;
        }

        private void TransportLayerSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string value = (e.AddedItems[0] as ComboBoxItem).Content as string;
            if (!string.IsNullOrEmpty(value))
                TransportLayer = value;

        }

        private void DisconnectClicked(object sender, RoutedEventArgs e)
        {
            client.Disconnect();
            //OnDisconnected();

        }
       



        private async void TryPunchHole(object sender, RoutedEventArgs e)
        {

            try
            {
                var res = await client.RequestHolePunchAsync(peerstoSend.First(), 5000);
                if (!res)
                    DispatcherRun(() => { Status.Text += "+\nHolePunchFailed"; });

                else
                    DispatcherRun(() => { Status.Text += "+\nHoleSucess!"; });
            }
            catch (Exception ee)
            {
                DispatcherRun(() => { Status.Text += "\nError: " + ee.Message; });

            }


        }

        private void QualitySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue < 9.5)
                videoHandler.compressionLevel = (int)(e.NewValue * 10);

        }

        private void FPSSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

            videoHandler.captureRateMs = (int)(1000 / e.NewValue);

        }
        private void VolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            audioHandler.Gain = (float)e.NewValue;
        }

        private void SendDoubleAudioChecked(object sender, RoutedEventArgs e)
        {
            audioHandler.SendTwice = (bool)((CheckBox)sender).IsChecked;

        }

        private void BufferDurationSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            audioHandler.BufferLatency = (int)e.NewValue;
        }
        #endregion

        private void OnDisconnected()
        {
            ///AsyncToastNotificationHandler.ShowInfoNotification("Disconnected from server", 10000);
            CallStateManager.EndCall();
            DispatcherRun(() => { IsConnected = false; Status.Text += "+\nDisconnected.."; });
        }

        private void DispatcherRun(Action todo)
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(todo);

            }
            catch
            {
            }
        }

        #region File Transfer
        private void MessageWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                FileDirectoryStructure tree = FileShare.CreateDirectoryTree(files[0]);

                client.SendAsyncMessage(peerstoSend.First(), tree);

                List<FileTransfer> fileDatas = FileShare.GetFiles(tree);
                Task.Run(async () =>
                {
                foreach (var item in fileDatas)
                {
                        DispatcherRun(() => FTProgressText.Content = "Sending file: " +item.FilePath);
                        await client.SendRequestAndWaitResponse(peerstoSend.First(), item,null, 60000);
                        DispatcherRun(() => MessageWindow.Text += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + "You" + " : " + "Sent file: " + item.FilePath) ;
                        
                    }
                    DispatcherRun(() => FTProgressText.Content = "");


                });
                

            }

        }

        private void HandleFile(MessageEnvelope message)
        {
            if(message.Header == "FileTransfer")
            {
                var fileMsg = FileShare.HandleFileTransferMessage(message);
                DispatcherRun(() => FTProgressText.Content = string.Format(""));

                DispatcherRun(() => MessageWindow.Text += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + "You" + " : " + "received a file "+fileMsg.FilePath);
                var response = new MessageEnvelope();
                response.MessageId = message.MessageId;
                response.Header = "FileAck";

                client.SendAsyncMessage(message.From, response);

            }
            else
            {

                var fileTree = FileShare.HandleDirectoryStructure(message);
                int howManyFiles = fileTree.FileStructure.Values.Select(x => x.Count).Sum();
                DispatcherRun(() => FTProgressText.Content = string.Format("Incoming {0} files",howManyFiles));

                //DispatcherRun(()=> MessageWindow.Text += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + "You" + " : " + "received structure");
            }

        }
        #endregion


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            cameraWindow.Close();
            this.Close();
        }


        private void Window_StateChanged(object sender, EventArgs e)
        {
            switch (this.WindowState)
            {
                case WindowState.Maximized:
                    cameraWindow.Hide();

                    break;
                case WindowState.Minimized:
                    
                    cameraWindow.Show();
                    if(cameraWindow.Owner == null)
                        cameraWindow.Owner = this;

                    break;
                case WindowState.Normal:
                    cameraWindow.Hide();

                    break;
            }
        }
       

      
        public void ExpandImageCanvas()
        {
            DispatcherRun(() =>
            {
                ChatColumn = 280;
                CanvasColumn = -1;
                ChatColumn = 280;
                CanvasColumn = -1;
            });
            
        }

        public void CollapseImageCanvas()
        {
            DispatcherRun(() =>
            {
                CanvasColumn = 0;
                ChatColumn = -1;
                CanvasColumn = 0;
                ChatColumn = -1;
            });
        }
    }
    }

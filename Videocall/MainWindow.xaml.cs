using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Linq;
using NAudio.Wave;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Protobuff;
using Protobuff.P2P;

namespace Videocall
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
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
        private HashSet<Guid> peerstoSend = new HashSet<Guid>();

        AudioHandler audioHandler;
        VideoHandler videoHandler = new VideoHandler();

        private bool loopbackAudio;
        private string TransportLayer = "Tcp";

        public MainWindow()
        {
            DataContext = this;

            InitializeComponent();

            var cert = new X509Certificate2("client.pfx", "greenpass");
            client = new RelayClient(cert);

            client.OnMessageReceived += ClientMsgRecieved;
            client.OnUdpMessageReceived += CluendUdpMessageReceived ;

            audioHandler = new AudioHandler();
            audioHandler.OnAudioAvailable += OnAudioFromMicAvailable;

            audioHandler.StartMic();
            audioHandler.StartSpeakers();

            videoHandler.OnImageAvailable += OnVideoImageAvailable;

            client.OnPeerRegistered += PeerRegistered;
            client.OnPeerUnregistered += PeerUnRegistered;
            soundQuality = "Medium";
            
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_ProcessExit;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            Application.Current.Exit += Current_Exit;

            client.OnDisconnected += OnDisconnected;
            Connect();
            //PeerInfos.Add(new PeerInfo("ASADA", Guid.NewGuid()));
        }

       

        private void OnVideoImageAvailable(byte[] imageBytes, Mat frame)
        {
            DispatcherRun(() =>
            {
                if(CamChecked)
                    SecondaryVideoCanvas.Source = frame.Flip(FlipMode.Y).ToBitmapSource();
            });
            //DispatcherRun(() =>
            //{
            //    var src = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged).ToBitmapSource();
            //    
            //    MainVideoCanvas.Source = src;

            //});

            foreach (var item in peerstoSend)
            {
                if (TransportLayer == "Udp")
                    client.SendUpMesssage(item, imageBytes, "Image");
                else
                    client.SendAsyncMessage(item, imageBytes, "Image");
            }
        }

        private void OnAudioFromMicAvailable(byte[] arg1, int arg2, int arg3)
        {
            if (loopbackAudio)
            {
                audioHandler.ProcessAudio(arg1, arg2, arg3);

            }

            if (!Soundchecked)
                return;
            foreach (var peerId in peerstoSend)
            {
                if (TransportLayer == "Udp")
                    client.SendUpMesssage(peerId, arg1, arg2, arg3, "Sound");

                else
                    client.SendAsyncMessage(peerId, arg1, arg2, arg3, "Sound");


            }
        }

        private void CluendUdpMessageReceived(MessageEnvelope message)
        {
            HandleMessage( message);
        }

        private void ClientMsgRecieved(MessageEnvelope message)
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
                case "Sound":
                    HandleIncomingSound(message);
                    break;
                case "Image":
                    HandleIncomingImage(message);
                    break;
                case "Text":
                    HandleText(message);
                    break;
            }
        }

        private void SendId(MessageEnvelope message)
        {
            DispatcherRun(() =>
            {
                var response = new MessageEnvelope();
                response.MessageId = message.MessageId;
                response.Header = "ID";
                response.KeyValuePairs = new Dictionary<string, string>();
                response.KeyValuePairs.Add(client.sessionId.ToString(), UserName.Text);
                client.SendAsyncMessage(message.From, response);
            });


        }

        private void HandleIncomingImage(MessageEnvelope message)
        {
            DispatcherRun(() =>
            {
                var src = Cv2.ImDecode(message.Payload, ImreadModes.Unchanged).ToBitmapSource();
                
                MainVideoCanvas.Source = src;

            });
        }

        private void HandleIncomingSound(MessageEnvelope message)
        {
            audioHandler.ProcessAudio(message.Payload);
        }

        private void HandleText(MessageEnvelope message)
        {
            foreach (var item in message.KeyValuePairs)
            {
                DispatcherRun(() => { MessageWindow.Text += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + item.Key + " : " + item.Value; });

            }

        }

        private void PeerRegistered(Guid obj)
        {
            peerstoSend.Add(obj);

            Task.Run(async () =>
            {
                MessageEnvelope env = new MessageEnvelope();
                env.Header = "Who";
                var response = await client.SendRequestAndWaitResponse(obj, env, 3000);
                if (response.Header != MessageEnvelope.RequestTimeout)
                {
                    string name = response.KeyValuePairs[obj.ToString()];
                    peers.TryAdd(obj, name);
                    DispatcherRun(() => PeerInfos.Add(new PeerInfo(name, obj)));
                }
            });
           
        }
        private void PeerUnRegistered(Guid obj)
        {
            DispatcherRun(() =>
            {
                peerstoSend.Remove(obj);
                peers.TryRemove(obj, out var peerName);
                var info = PeerInfos.Where(x => x.Guid == obj).FirstOrDefault();

                if (info != null)
                    PeerInfos.Remove(info);
            });
           
        }

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


        private WaveFormat GetSoundQuality()
        {
            switch (soundQuality)
            {
                case "Low":
                    return new WaveFormat(48000, 16, 1);
                    break;
                case "Medium":
                    return new WaveFormat(48000, 16, 2);
                    break;
                case "High":
                    return new WaveFormat(96000, 16, 2);
                    break;
                case "VeryHigh":
                    return new WaveFormat(192000, 24, 2);
                    break;
                default:
                    return new WaveFormat(48000, 16, 2);
                    break;
            }

        }

      

        private  void ConnectButtonClick(object sender, RoutedEventArgs e)
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
       

        private void SoundQuality_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            soundQuality = (e.AddedItems[0] as ComboBoxItem).Content as string;
        }

        private void CameraButton_Checked(object sender, RoutedEventArgs e)
        {
            if ((sender as ToggleButton).IsChecked ==true)
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

        private void SoundButton_Checked(object sender, RoutedEventArgs e)
        {
            if ((sender as ToggleButton).IsChecked == true)
            {
                Soundchecked = true;
                return;
            }
            Soundchecked = false;

        }

        private void SendMessageButtonClicked(object sender, RoutedEventArgs e)
        {
            string textToSend = ChatMessageBox.Text;
            MessageEnvelope msg = new MessageEnvelope();
            msg.KeyValuePairs = new Dictionary<string, string>();
            msg.KeyValuePairs[UserName.Text] = textToSend;
            msg.Header = "Text";

            MessageWindow.Text += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + "You" + " : " + textToSend;


            foreach (var peer in peerstoSend)
            {
                client.SendAsyncMessage(peer, msg);

            }
            ChatMessageBox.Text = "";
            
        }

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                SendMessageButtonClicked(null,null);
            }
        }

        private void ListenYourselfToggled(object sender, RoutedEventArgs e)
        {
            loopbackAudio = (bool)((CheckBox)sender).IsChecked;
        }

        private void TransportLayerSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TransportLayer = (e.AddedItems[0] as ComboBoxItem).Content as string;

        }

        private void DisconnectClicked(object sender, RoutedEventArgs e)
        {
            client.Disconnect();
            OnDisconnected();

        }
        private void OnDisconnected()
        {
            DispatcherRun(() => { IsConnected = false; Status.Text += "+\nDisconnected.."; });
        }

        private void QualitySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if(e.NewValue<9d)
                videoHandler.compressionLevel = (int)(e.NewValue * 10);

        }

        private void FPSSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            
            videoHandler.captureRateMs = (int)(1000/e.NewValue );

        }

        private async void TryPunchHole(object sender, RoutedEventArgs e)
        {
            var res = await client.RequestHolePunchAsync(peerstoSend.First(),5000);
            if(!res)
                DispatcherRun(() => {  Status.Text += "+\nHolePunchFailed"; });

            else
                DispatcherRun(() => { Status.Text += "+\nHoleSucess!"; });

        }

        private void UserName_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
    }

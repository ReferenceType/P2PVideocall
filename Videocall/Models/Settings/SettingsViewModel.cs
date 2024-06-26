﻿using ProtoBuf;
using ServiceProvider.Services.Audio;
using ServiceProvider.Services.Audio.Dependency;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Videocall.Models;
using Videocall.Services.ScreenShare;
using Videocall.Services.Video;
using Videocall.Services.Video.H264;
using Videocall.UserControls;

namespace Videocall.Settings
{
    public class SettingsViewModel : PropertyNotifyBase
    {
        private static SettingsViewModel instance;
        public static SettingsViewModel Instance
        {
            get
            {
                if (instance == null)
                    instance = new SettingsViewModel();
                return instance;
            }
        }

        public ICommand ConnectButtonClickCommand { get; }
        public ICommand DisconnectButtonClickCommand { get; }
        public ICommand HolePunchClickCommand { get; }
        public ICommand ClearChatHistoryCommand { get; }
        public ICommand ApplyCameraSettingsCmd { get; }
        public ICommand SearchServerClicked { get; }
        public ICommand ResetDevices { get; }


        private ComboBoxItem fTTransportLayer = new ComboBoxItem();

        private string logText;

        private string tcpLatency;
        private string udpLatency;
        private string totalNumLostPackages;
        private string imageTransferRate;
        private string incomingImageDataRate;
        private string outgoingFrameRate;
        private string incomingFrameRate;
        private string maxBps;
        private double fpsSliderValue = 23;
        private double volumeValue = 1;
        private double bufferDurationValue = 200;
        private bool cameraChecked;
        private bool testSCChecked;
        private bool listenYourselfCheck = false;
        private bool sendDoubleAudiocheck = false;


        private int bufferedDurationPercentage;
        private int congestion = 0;
        private SoundSliceData soudVisualData;

        #region Properties

        public string LogText
        {
            get => logText; set { logText = value; OnPropertyChanged(); }
        }
        
        public ComboBoxItem FTTransportLayer
        {
            get => fTTransportLayer; set
            {
                if (value.Content == null) return;
                fTTransportLayer = value;

                HandleFTTransportLayerChanged(fTTransportLayer.Content.ToString());
                OnPropertyChanged();
            }
        }

        public double FpsSliderValue
        {
            get => fpsSliderValue;
            set
            {
                fpsSliderValue = value;
                HandleFpsSliderChanged(value);
                OnPropertyChanged();
            }
        }

        public double VolumeValue
        {
            get => volumeValue;
            set
            {
                volumeValue = value;
                HandleVolumeSliderChanged(value);
                OnPropertyChanged();
            }
        }


        public double BufferDurationValue
        {
            get => bufferDurationValue;
            set
            {
                bufferDurationValue = value;
                HandleBufferDurationChanged(value);
                OnPropertyChanged();
            }
        }

        private bool listenYourselfEnabled=true;
        public bool ListenYourselfEnabled { get => listenYourselfEnabled; set { listenYourselfEnabled = value; OnPropertyChanged(); } }

        public bool ListenYourselfCheck
        {
            get => listenYourselfCheck; set
            {
                listenYourselfCheck = value;
                HandleListenYourselfToggle(value);
                OnPropertyChanged();
            }
        }
        // Debug Only
        public bool SendReliable
        {
            get => sendReliable; set
            {
                sendReliable = value;
                HandleSendReliableToggle(value);
                OnPropertyChanged();
            }
        }

        public bool SendDoubleAudiocheck
        {
            get => sendDoubleAudiocheck;
            set
            {
                sendDoubleAudiocheck = value;
                HandleSenddoubleAudioChecked(value);
                OnPropertyChanged();
            }
        }
        #endregion


        private ServiceHub services;
        private bool holePunchRequestActive;
        private string averageLatency;
        private bool sendReliable = false;
        public ObservableCollection<DeviceInfo> InputDevices { get; set; } = new ObservableCollection<DeviceInfo>();
        public DeviceInfo SelectedDevice { 
            get => selectedDevice;
            set 
            {
                if (selectedDevice == value)
                {
                    OnPropertyChanged();
                    return;
                }

                selectedDevice = value;
                services.AudioHandler.SelectedDevice = value; 
                services.AudioHandler.ChangeDevice();
                OnPropertyChanged();
            }
        }
        private int selectedDeviceIndex;
        public int SelectedDeviceIndex
        {
            get => selectedDeviceIndex;
            set 
            {
                selectedDeviceIndex = value;
                OnPropertyChanged();
            }
        }

        public PersistentSettingConfig Config { get; set; } = PersistentSettingConfig.Instance;
        public string TcpLatency { get => tcpLatency; set { tcpLatency = value; OnPropertyChanged(); } }
        public string UdpLatency { get => udpLatency; set { udpLatency = value; OnPropertyChanged(); } }

        public string TotalNumLostPackages { get => totalNumLostPackages; set { totalNumLostPackages = value; OnPropertyChanged(); } }

        public int BufferedDurationPercentage { get => bufferedDurationPercentage; set { bufferedDurationPercentage = value; OnPropertyChanged(); } }
        public string ImageTransferRate { get => imageTransferRate; set { imageTransferRate = value; OnPropertyChanged(); } }
        public string IncomingImageDataRate { get => incomingImageDataRate; set { incomingImageDataRate = value; OnPropertyChanged(); } }
        public string MaxBps { get => maxBps; set { maxBps = value; OnPropertyChanged(); } }

        public string OutgoingFrameRate { get => outgoingFrameRate; set { outgoingFrameRate = value; OnPropertyChanged(); } }
        public string IncomingFrameRate { get => incomingFrameRate; set { incomingFrameRate = value; OnPropertyChanged(); } }

        public string AverageLatency { get => averageLatency; private set { averageLatency = value; OnPropertyChanged(); } }
        public ComboBoxItem SCResolution { get; set; } = new ComboBoxItem();
        public ComboBoxItem H264Config { get; set; } = new ComboBoxItem();

        public bool TestCamChecked
        {
            get => cameraChecked;
            set
            {
                cameraChecked = value;
                if (value)
                {
                    Task.Run(() => services.VideoHandler.ObtainCamera()).ContinueWith((t) =>
                        services.VideoHandler.StartCapturing());
                }
                else
                {
                    services.VideoHandler.CloseCamera();

                }
                OnPropertyChanged();
            }
        }
        public bool TestSCChecked
        {
            get => testSCChecked;
            set
            {
                testSCChecked = value;
                if (value)
                {
                    services.ScreenShareHandler.StartCapture();
                }
                else
                {
                    services.ScreenShareHandler.StopCapture();

                }
                OnPropertyChanged();
            }
        }
        public bool RectifiedSignalChecked { get => rectifiedSignalChecked; set
            {
                rectifiedSignalChecked = value;
                OnPropertyChanged();
                services.AudioHandler.RectifySignal = value;

            }
        }

        public bool EnableSoundVisualPublish
        {
            get => enableSoundVisualPublish; set
            {
                enableSoundVisualPublish = value;
                OnPropertyChanged();
                services.AudioHandler.EnableSoundVisualData = value;

            }
        }
        public int Congestion { get => congestion; set { congestion = value; OnPropertyChanged(); } }

        public SoundSliceData SoudVisualData { get => soudVisualData; set { soudVisualData = value; OnPropertyChanged(); } }


        private bool enableSoundVisualPublish;

        private bool rectifiedSignalChecked;
        private SettingsViewModel()
        {
            var services = ServiceHub.Instance;
            instance = this;
            this.services = services;
            SearchServerClicked = new RelayCommand(HandleServerSearch);
            ConnectButtonClickCommand = new RelayCommand(HandleConnectRequest);
            DisconnectButtonClickCommand = new RelayCommand(OnDisconnectClicked);
            HolePunchClickCommand = new RelayCommand(OnHolePunchClicked);
            ClearChatHistoryCommand = new RelayCommand(HandleClearChatHistory);
            ApplyCameraSettingsCmd = new RelayCommand(ApplyCamSettings);
            ResetDevices = new RelayCommand((x)=>services.AudioHandler.ResetDevices());

            foreach (var dev in services.AudioHandler.InputDevices) {
                InputDevices.Add(dev);
            }
            services.AudioHandler.InputDevicesUpdated += () => {
                DispatcherRun(() =>
                {
                    InputDevices.Clear();
                    foreach (var dev in services.AudioHandler.InputDevices)
                    {
                        InputDevices.Add(dev);
                    }
                    SelectedDeviceIndex = InputDevices.IndexOf(services.AudioHandler.SelectedDevice);
                    if (SelectedDeviceIndex == -1)
                    {
                        SelectedDeviceIndex = 0;
                    }

                   // Task.Delay(100).ContinueWith((t)=> DispatcherRun(() => SelectedDevice = services.AudioHandler.SelectedDevice));
                });
               
            };

            selectedDevice = services.AudioHandler.SelectedDevice;

            services.MessageHandler.OnDisconnected += OnDisconnected;
            services.LatencyPublisher.Latency += OnLatencyAvailable;

            services.AudioHandler.OnStatisticsAvailable += HandleAudioStatistics;
            services.AudioHandler.OnSoundLevelAvailable = SoundVisualDataAvailable;

            services.VideoStatisticsAvailable = HandleVideoStatistics;
            services.CamSizeFeedbackAvailable = HandleCamSizeFeedback;
            HandleConnectRequest(null);
            MainWindowEventAggregator.Instance.PeerRegistered += OnPeerRegistered;

            ApplyCamSettings(null);


        }

        private void HandleServerSearch(object obj)
        {
            AddLog($"Searching Relay Server on local network with port:{Config.Port}");
            services.MessageHandler.SearchRelayServer(Config.Port)
                .ContinueWith(epTask =>
                {
                    if (epTask.Result.Count == 0)
                    {
                        AddLog("\nUnable to find relay server on local network..");
                    }
                    else
                    {
                        foreach (var result in epTask.Result)
                        {
                            AddLog($"\nRelay Server Found [{result.Name}] : {result.Endpoint}");
                        }
                        //Config.Ip = epTask.Result.Address.ToString();
                    }
                });


        }

        private void HandleCamSizeFeedback(int w, int h)
        {
            if (Config.CamFrameHeight != h)
            {
                Config.CamFrameHeight = h;
            }
            if (Config.CamFrameWidth != w)
            {
                Config.CamFrameWidth = w;
            }
        }

        private void SoundVisualDataAvailable(SoundSliceData data)
        {
            DispatcherRun(() =>
            {
                SoudVisualData = data;
            });
        }

        private void HandleVideoStatistics(VCStatistics statistics)
        {
            ImageTransferRate = "Outgoing Data Rate:  " + statistics.TransferRate.ToString("N2") + " Kb/s";
            OutgoingFrameRate = "Outgoing Frame Rate: " + statistics.OutgoingFrameRate.ToString("N0") + " Fps";

            AverageLatency = "Average Latency:     " + statistics.AverageLatency.ToString("N1") + " ms";
            IncomingFrameRate = "Incoming Frame Rate: " + statistics.IncomingFrameRate.ToString("N0") + " Fps";
            IncomingImageDataRate = "Incoming Data Rate:  " + statistics.ReceiveRate.ToString("N0") + " Kb/s";

            if (Config.EnableCongestionAvoidance)
            {
                var drop = (Config.TargetBps * 1000 - statistics.CurrentMaxBitRate);
                var per = (float)drop / (float)(Config.TargetBps * 1000);
                Congestion = (int)(per * 100);
                MaxBps = "Max Kbps:" + statistics.CurrentMaxBitRate / 1000;
            }
            else
            {
                MaxBps = "";
                Congestion = 0;
            }


        }

        private void OnPeerRegistered(VCPeerInfo info)
        {
            if (Config.AutoHolepunch)
                AutoPunch(info.Guid);
        }

        private void HandleClearChatHistory(object obj)
        {
            MainWindowEventAggregator.Instance.InvokeClearChatEvent();
        }

        private void HandleAudioStatistics(AudioStatistics stats)
        {
            TotalNumLostPackages = "Total Lost Packages : " + stats.TotalNumDroppedPackages.ToString();
            BufferedDurationPercentage = (int)(((float)stats.BufferedDuration / (float)stats.BufferSize) * 100);
        }



        private void OnLatencyAvailable(object sender, Services.Latency.LatencyEventArgs e)
        {
            var sesId = services.MessageHandler.SessionId;
            if (e.UdpLatency != null && e.UdpLatency.ContainsKey(sesId))
                DispatcherRun(() => UdpLatency = "Server Udp Latency: " + e.UdpLatency[sesId].ToString("N1") + " ms");

            if (e.TcpLatency != null && e.TcpLatency.ContainsKey(sesId))
                DispatcherRun(() => TcpLatency = "Server Tcp Latency: " + e.TcpLatency[sesId].ToString("N1") + " ms");

        }

        private async void HandleConnectRequest(object obj)
        {
            try
            {
                AddLog("\nConnecting..");
                var addr = Dns.GetHostAddresses(Config.Ip);
                if (addr == null)
                    throw new Exception("No address found");
                bool result = await services.MessageHandler.ConnectAsync(addr[0].ToString(), Config.Port);
                if (result)
                    AddLog("\nConnected");
                else
                    AddLog("\nFailed");
            }
            catch (Exception e)
            {
                AddLog("\n[Error]" + e.Message);
                if (Config.AutoReconnect)
                    Task.Delay(3000).ContinueWith(s => HandleConnectRequest(null));
            }

        }
        private void OnDisconnectClicked(object obj)
        {
            CallStateManager.EndCall();
            services.MessageHandler.Disconnect();
        }
        private void OnDisconnected()
        {
            AddLog("\nDisconnected");
            CallStateManager.EndCall();
            if (Config.AutoReconnect)
                Task.Run(async () => { await Task.Delay(1000); HandleConnectRequest(null); });

        }

        private async void AutoPunch(Guid peerId)
        {
            if (peerId.CompareTo(services.MessageHandler.SessionId) > 0)
            {
                await Punch(peerId);
            }
            

          
        }

        private async void OnHolePunchClicked(object obj)
        {
            if (holePunchRequestActive)
            {
                AddLog("\nRequest in progress");
                return;
            }

            holePunchRequestActive = true;
            try
            {
                foreach (var peerId in services.MessageHandler.registeredPeers.Keys)
                {
                    await Punch(peerId);
                }
            }
            finally { holePunchRequestActive = false; }
        }
        bool punchActive = false;
        private DeviceInfo selectedDevice;

        private async Task Punch(Guid peerId)
        {
            if(punchActive)
            {
                AddLog("\nRequest in progress");
                return;
            }
            try
            {
                punchActive = true;
                var res = await services.MessageHandler.RequestHolePunchAsync(peerId, 5000);
                var res2 = await services.MessageHandler.RequestTcpHolePunchAsync(peerId, 5000);
                if (!res)
                    AddLog("\nUDP HolePunch Failed on :" + peerId.ToString());

                else
                    AddLog("\nUDP HolePunch Sucessfull");
                if (!res2)
                    AddLog("\nTCP HolePunch Failed on :" + peerId.ToString());

                else
                    AddLog("\nTCPHolePunch Sucessfull");
            }
            catch (Exception ee)
            {
                AddLog("\nError: " + ee.Message);
            }
            finally
            {
                punchActive = false;
            }
        }
    

        private void HandleListenYourselfToggle(bool value)
        {
            if(!CallStateManager.IsOnACall)
                services.AudioHandler.LoopbackAudio = value;
        }

        private void HandleSendReliableToggle(bool value)
        {
           // services.AudioHandler.LoopbackAudio = value;
        }
      
        private void HandleFTTransportLayerChanged(string value)
        {
            services.MessageHandler.FTTransportLayer = value;
        }

        private void HandleFpsSliderChanged(double value)
        {
            services.VideoHandler.CaptureIntervalMs = (int)(1000 / value);

        }

        private void HandleVolumeSliderChanged(double value)
        {
            services.AudioHandler.Gain = (float)value;
        }


        private void HandleSenddoubleAudioChecked(bool value)
        {
            services.AudioHandler.SendMultiStream = value;

        }

        private void HandleBufferDurationChanged(double value)
        {
            services.AudioHandler.JitterBufferMaxCapacity = (int)value;
            services.VideoHandler.VideoLatency = (int)value;

        }

        private void AddLog(string message)
        {
            DispatcherRun(() => { LogText += message; });
        }
        private void ApplyCamSettings(object obj)
        {
            string config = "Default";
            if (H264Config.Content != null)
            {
                 config = H264Config.Content.ToString();
            }
            services.VideoHandler.EnableCongestionAvoidance = Config.EnableCongestionAvoidance;
            services.VideoHandler.ApplySettings(Config.CamFrameWidth, Config.CamFrameHeight, Config.TargetBps, Config.IdrInterval, Config.CameraIndex,Config.MinBps);
            string resolution = null;
            if (SCResolution.Content != null)
                resolution = SCResolution.Content.ToString();

            services.ScreenShareHandler.ScreenId = Config.ScreenId;
            services.ScreenShareHandler.GpuId = Config.GpuId;
            services.ScreenShareHandler.EnableParalelisation = Config.MultiThreadedScreenShare;
            services.ScreenShareHandler.ApplyChanges(resolution, Config.SCTargetFps,Config.SCTargetBps,config);
            
        }
        //private void CallStateChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        //{
        //    var currState = CallStateManager.GetState();
        //    if (currState == CallStateManager.CallState.OnCall)
        //    {
        //        Task.Run(() => { services.ResetBuffers(); });

               
        //    }
        //    if(currState== CallStateManager.CallState.Available)
        //    {
        //        Task.Run(() => 
        //        {
        //            services.ResetBuffers();
        //            services.VideoHandler.HardReset();
        //            services.AudioHandler.StopSpreakers();
        //        });

                   
        //    }
        //}
        private void DispatcherRun(Action todo)
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(todo);
            }
            catch(Exception e)
            {
                DebugLogWindow.AppendLog("Error Dispatcher",e.Message);
            }
        }

    }
}

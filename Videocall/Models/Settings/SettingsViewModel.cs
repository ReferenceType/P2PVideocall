using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Videocall.Services.HttpProxy;
using Windows.Media.Capture;

namespace Videocall.Settings
{
    public class SettingsViewModel:PropertyNotifyBase
    {
        public ICommand ConnectButtonClickCommand { get; }
        public ICommand DisconnectButtonClickCommand { get; }
        public ICommand HolePunchClickCommand { get; }
        public ICommand HTTPProxtIpCommand { get; }


        private ComboBoxItem transportLayer;
        private string logText;

        private string tcpLatency;
        private string udpLatency;
        private string totalNumLostPackages;
        private string packageLossRate;

        private double fpsSliderValue = 30;
        private double imageQualitySliderValue=8.3d;
        private double volumeValue = 3;
        private double bufferDurationValue = 200;

        private int bufferedDurationPercentage;

        private bool listenYourselfCheck = false;
        private bool sendDoubleAudiocheck;

        #region Properties
        public string LogText
        {
            get => logText; set { logText = value; OnPropertyChanged(); }
        }

        public ComboBoxItem TransportLayer { get => transportLayer; set 
            {
                if (value.Content == null) return;
                transportLayer = value;
                
                HandleTransportLayerChanged(transportLayer.Content.ToString());
                OnPropertyChanged(); 
            } 
        }

        public double FpsSliderValue { get => fpsSliderValue; 
            set
            {
                fpsSliderValue = value;
                HandleFpsSliderChanged(value);
                OnPropertyChanged();
            }
        }

        public double ImageQualitySliderValue { get => imageQualitySliderValue; 
            set 
            {
                imageQualitySliderValue = value;
                HandleImageQualitySliderChanged(value);
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

       
        public double BufferDurationValue { get => bufferDurationValue; 
            set
            {
                bufferDurationValue = value;
                HandleBufferDurationChanged(value);
                OnPropertyChanged();
            } 
        }

        

        public bool ListenYourselfCheck { get => listenYourselfCheck; set 
            { 
                listenYourselfCheck = value;
                HandleListenYourselfToggle(value); 
                OnPropertyChanged(); 
            } 
        }

      

        public bool SendDoubleAudiocheck { get => sendDoubleAudiocheck; 
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

        public SettingConfig Config { get; set; } = SettingConfig.Instance;
        public string TcpLatency { get => tcpLatency; set { tcpLatency = value; OnPropertyChanged(); } }
        public string UdpLatency { get => udpLatency; set { udpLatency = value; OnPropertyChanged(); } }

        public string TotalNumLostPackages { get => totalNumLostPackages; set { totalNumLostPackages = value; OnPropertyChanged(); } }
        public string PackageLossRate { get => packageLossRate; set { packageLossRate = value; OnPropertyChanged(); } }

        public int BufferedDurationPercentage { get => bufferedDurationPercentage; set { bufferedDurationPercentage = value; OnPropertyChanged(); } }

        internal SettingsViewModel(ServiceHub services)
        {
            this.services = services;
            ConnectButtonClickCommand = new RelayCommand(HandleConnectRequest);
            DisconnectButtonClickCommand = new RelayCommand(OnDisconnectClicked);
            HolePunchClickCommand = new RelayCommand(OnHolePunchClicked);
            HTTPProxtIpCommand = new RelayCommand(HandleProxyIpRequested);


            services.MessageHandler.client.OnDisconnected += OnDisconnected;
            services.LatencyPublisher.Latency += OnLatencyAvailable;

            services.AudioHandler.OnStatisticsAvailable += HandleAudioStatistics;

            HandleConnectRequest(null);
        }

        private void HandleAudioStatistics(AudioStatistics stats)
        {
            TotalNumLostPackages = "Total Lost Packages : "+stats.TotalNumDroppedPAckages.ToString();
            PackageLossRate = "Lost Package Rate/s : "+stats.NumLostPackages;

            BufferedDurationPercentage = (int)(((float)stats.BufferedDuration / (float)stats.BufferSize) *100);
        }

        private async void HandleProxyIpRequested(object obj)
        {
            try
            {
                string ip = await IpRetriever.ObtainIp();
                if (ip == null)
                    DispatcherRun(() => { LogText += "\nUnable To Retrieve Ip"; });
                else
                    SettingConfig.Instance.Ip = ip;
                DispatcherRun(() => { LogText += "\nSucessfully retrieved  the IP: " +ip; });

            }
            catch (Exception ex)
            {
                DispatcherRun(() => { LogText += "\nUnable To Retrieve Ip: " +ex.Message; });
            }
        }

        private void OnLatencyAvailable(object sender, Services.Latency.LatencyEventArgs e)
        {
            var sesId = services.MessageHandler.client.sessionId;
            if (e.UdpLatency!=null && e.UdpLatency.ContainsKey(sesId) )
                DispatcherRun(() => UdpLatency = "Server Udp Latency: " + e.UdpLatency[sesId].ToString("N1") + " ms");

            if (e.TcpLatency != null && e.TcpLatency.ContainsKey(sesId))
                DispatcherRun(() => TcpLatency = "Server Tcp Latency: " + e.TcpLatency[sesId].ToString("N1") + " ms");
            
        }

        private async void HandleConnectRequest(object obj)
        {
            try
            {
                AddLog( "\nConnecting..");
                await services.MessageHandler.client.ConnectAsync(Config.Ip, int.Parse(Config.Port));
                AddLog("\nConnected");

            }
            catch
            {
                AddLog("\nError..");
            }
        }
        private void OnDisconnectClicked(object obj)
        {
            services.MessageHandler.client.Disconnect();
        }
        private void OnDisconnected()
        {
            AddLog("\nDisconnected");
            CallStateManager.EndCall();
            
        }
        private async void OnHolePunchClicked(object obj)
        {
            if (holePunchRequestActive) 
                return;

            holePunchRequestActive = true;
            foreach (var peerId in services.MessageHandler.registeredPeers)
            {
                try
                {
                    var res = await services.MessageHandler.client.RequestHolePunchAsync(peerId, 5000);
                    if (!res)
                        AddLog( "\nHolePunch Failed on :" + peerId.ToString());

                    else
                        AddLog("\nHolePunch Sucessfull");
                }
                catch (Exception ee)
                {
                    AddLog("\nError: " + ee.Message);
                }
                finally { holePunchRequestActive = false; }
            }

        }


        private void HandleListenYourselfToggle(bool value)
        {
            services.AudioHandler.LoopbackAudio = value;
        }

        private void HandleTransportLayerChanged(string value)
        {
            services.MessageHandler.TransportLayer = value;
        }

        private void HandleImageQualitySliderChanged(double value)
        {
            services.VideoHandler.compressionLevel = (int)(value * 10);
        }

        private void HandleFpsSliderChanged(double value)
        {
            services.VideoHandler.captureRateMs = (int)(1000 / value);

        }

        private void HandleVolumeSliderChanged(double value)
        {
            services.AudioHandler.Gain = (float)value;
        }


        private void HandleSenddoubleAudioChecked(bool value)
        {
           services.AudioHandler.SendTwice = value;

        }

        private void HandleBufferDurationChanged(double value)
        {
            services.AudioHandler.BufferLatency = (int)value;
            services.VideoHandler.VideoLatency = (int)value;

        }
       
        private void AddLog(string message)
        {
            DispatcherRun(() => { LogText += message; });
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

    }
}

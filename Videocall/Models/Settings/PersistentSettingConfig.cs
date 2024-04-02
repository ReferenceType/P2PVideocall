
using ProtoBuf.Meta;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Videocall.Services.File_Transfer;

namespace Videocall
{
    //{"Ip":"82.61.88.82","Port":"20011","Name":"CinnamonBun"}
    // all props goes to disk as json
    public class PersistentSettingConfig : INotifyPropertyChanged
    {
        public static PersistentSettingConfig instance;
        private string ip;
        private int port;
        private string name;
        private int fTWindowSize = 2560000;

        private int chunkSize = 127000;
        private bool autoReconnect = true;
        private bool autoHolepunch = true;
        private int targetBps = 1500;
        private int minBps = 300;
        private int sctargetBps = 2000;
        private static bool dontInvoke = true;
        private int screenId =0;
        private int gpuId = 0;
        private int idrInterval = -1;
        private int sCTargetFps = 15;
        private bool multiThreadedScreenShare = false;
        private int cameraIndex = 0;
        private int camFrameWidth = 640;
        private int camFrameHeight = 480;

        public event PropertyChangedEventHandler PropertyChanged;

        public static PersistentSettingConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = DeserializeToJsonAndLoad();
                    dontInvoke = false;
                }
                return instance;
            }
        }
      
        public string Ip { get => ip; set { ip = value; OnPropertyChanged(); } }
        public int Port { get => port; set { port = value; OnPropertyChanged(); } }

        public string Name { get => name; set { name = value; OnPropertyChanged(); } }
        public int FTWindowSize { get => fTWindowSize; 
            set 
            {
                fTWindowSize = value;
                FileTransferStateManager.WindowsSize = fTWindowSize;
            }
        }

        public int ChunkSize { get => chunkSize; 
            set 
            { 
                chunkSize = value; OnPropertyChanged();
                FileTransferStateManager.ChunkSize = chunkSize;
            }
        }
        public bool AutoReconnect { get => autoReconnect; set { autoReconnect = value; OnPropertyChanged(); } }
        public bool AutoHolepunch { get => autoHolepunch; set { autoHolepunch = value; OnPropertyChanged(); } }
        public int CameraIndex { get => cameraIndex; set {
                cameraIndex = value;
                OnPropertyChanged();
            } }
        public int CamFrameWidth { get => camFrameWidth; set { camFrameWidth = value; OnPropertyChanged(); } }
        public int CamFrameHeight { get => camFrameHeight; set { camFrameHeight = value; OnPropertyChanged(); } }
        public int ScreenId { get => screenId; set { screenId = value; OnPropertyChanged(); } }
        public int GpuId { get => gpuId; set { gpuId = value; OnPropertyChanged(); } }
        public int TargetBps { get => targetBps; set { targetBps = value; OnPropertyChanged(); } }
        public int MinBps { get => minBps; set { minBps = value; OnPropertyChanged(); } }
        public int IdrInterval { get => idrInterval; set {idrInterval = value; OnPropertyChanged();  } }
        public int SCTargetBps { get => sctargetBps; set { sctargetBps = value; OnPropertyChanged(); } }
        public int SCTargetFps { get => sCTargetFps; set { sCTargetFps = value; OnPropertyChanged(); } }
        public bool MultiThreadedScreenShare { get => multiThreadedScreenShare; set {
                multiThreadedScreenShare = value;
                ServiceHub.Instance.ScreenShareHandler.EnableParalelisation = multiThreadedScreenShare;
                OnPropertyChanged();
            } }
            public bool UseWasapi
        {
            get => useWasapi;
            set
            {
                useWasapi = value;
                //ServiceHub.Instance.AudioHandler.useWasapi = value;
                OnPropertyChanged();
            }
        }
        public bool EnableCongestionAvoidance { get => enableCongestionAvoidance; 
            set { enableCongestionAvoidance = value; 
                ServiceHub.Instance.VideoHandler.EnableCongestionAvoidance = enableCongestionAvoidance;
                OnPropertyChanged(); 
            } }

        public bool ReliableIDR { get => reliableIDR;  set
            {
                reliableIDR = value;
                OnPropertyChanged();
            }
        }
        private int transportLayer = 0;

        public int TransportLayer
        {
            get => transportLayer; set
            {
                transportLayer = value;
                string layer = "";
                switch (transportLayer)
                {
                    case 0:
                        layer = "Udp";
                        break;
                    case 1:
                        layer = "Tcp";
                        break;
                    default:
                        layer = "Udp";
                        break;
                }
                HandleTransportLayerChanged(layer);
                OnPropertyChanged();
            }
        }
        public bool AutoAcceptCalls { get => autoAcceptCalls; set { autoAcceptCalls = value; OnPropertyChanged(); } }


        private bool autoAcceptCalls = false;
        private bool reliableIDR = true;
        private bool enableCongestionAvoidance = true;
        private bool useWasapi = false;

        public static void SerializeToJsonAndSave()
        {
            string jsonString = JsonSerializer.Serialize(Instance);
            string workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = workingDir+ "/Settings/Settings.json";
            File.WriteAllText(path, jsonString);
        }

        public static PersistentSettingConfig DeserializeToJsonAndLoad()
        {
            string workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = workingDir+ "/Settings/Settings.json";
            if (!File.Exists(path))
            {
                PersistentSettingConfig cnf = new PersistentSettingConfig();
                string jsonString = JsonSerializer.Serialize(cnf);
                var dir = Path.GetDirectoryName(path);
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, jsonString);
            }
            string jsonText = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PersistentSettingConfig>(jsonText);
        }
        private void HandleTransportLayerChanged(string value)
        {
            ServiceHub.Instance.MessageHandler.TransportLayer = value;
        }
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            if (dontInvoke) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            SerializeToJsonAndSave();
        }
    }
}

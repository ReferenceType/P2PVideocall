
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Videocall
{
    //{"Ip":"82.61.88.82","Port":"20011","Name":"CinnamonBun"}
    // all props goes to disk as json
    public class PersistentSettingConfig : INotifyPropertyChanged
    {
        public static PersistentSettingConfig instance;
        private string ip;
        private string port;
        private string name;
        private string chunkSize = "1000000";
        private bool autoReconnect = true;
        private bool autoHolepunch = true;
        private static bool dontInvoke = true;
        
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
        public string Port { get => port; set { port = value; OnPropertyChanged(); } }

        public string Name { get => name; set { name = value; OnPropertyChanged(); } }
        public string ChunkSize { get => chunkSize; set { chunkSize = value; OnPropertyChanged(); } }
        public bool AutoReconnect { get => autoReconnect; set { autoReconnect = value; OnPropertyChanged(); } }
        public bool AutoHolepunch { get => autoHolepunch; set { autoHolepunch = value; OnPropertyChanged(); } }

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

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            if (dontInvoke) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            SerializeToJsonAndSave();
        }
    }
}

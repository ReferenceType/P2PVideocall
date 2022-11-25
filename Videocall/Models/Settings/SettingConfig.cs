
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Videocall
{
    //{"Ip":"82.61.88.82","Port":"20011","Name":"CinnamonBun"}

    public class SettingConfig : INotifyPropertyChanged
    {
        public static SettingConfig instance;
        private string ip;
        private string port;
        private string name;
        private static bool dontInvoke = true;
        public event PropertyChangedEventHandler PropertyChanged;

        public static SettingConfig Instance
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

        public static void SerializeToJsonAndSave()
        {
            string jsonString = JsonSerializer.Serialize(Instance);
            string workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = workingDir+ "/Settings/Settings.json";
            File.WriteAllText(path, jsonString);
        }

        public static SettingConfig DeserializeToJsonAndLoad()
        {
            string workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = workingDir+ "/Settings/Settings.json";
            if (!File.Exists(path))
            {
                SettingConfig cnf = new SettingConfig();
                string jsonString = JsonSerializer.Serialize(cnf);
                var dir = Path.GetDirectoryName(path);
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, jsonString);
            }
            string jsonText = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SettingConfig>(jsonText);
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            if (dontInvoke) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            SerializeToJsonAndSave();
        }
    }
}

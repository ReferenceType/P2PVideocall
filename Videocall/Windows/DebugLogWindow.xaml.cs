using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace Videocall
{
    public class Log
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; }
        public string LogType { get; set; }
    }
    /// <summary>
    /// Interaction logic for DebugLogWindow.xaml
    /// </summary>
    public partial class DebugLogWindow : Window
    {
        private static DebugLogWindow instance;
        public static DebugLogWindow Instance
        { 
            get
            {
                if(instance == null)
                {
                    Application.Current.Dispatcher.Invoke(() => { instance = new DebugLogWindow(); });
                }
                return instance;
          } 
        }
        public static event EventHandler<PropertyChangedEventArgs> StaticPropertyChanged;
        protected static void OnPropertyChanged([CallerMemberName] string name = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(name));
        }

        private static string logText = "Test";
         
        public static string LogText { get => logText; set { logText = value; OnPropertyChanged(); } }

        public ObservableCollection<Log> Logs { get => logs; set { logs = value; OnPropertyChanged(); } }

        static StringBuilder sb = new StringBuilder();
        private ObservableCollection<Log> logs =
            new ObservableCollection<Log>();
        public DebugLogWindow()
        {
            instance= this;
            var chr = new WindowChrome();
            chr.ResizeBorderThickness = new Thickness(10, 10, 10, 10);
            WindowChrome.SetWindowChrome(this, chr);
            DataContext = this;
            InitializeComponent();
        }
        public static void AppendLog(string logType, string message)
        {
            Instance.AppendLog_(logType, message);
        }
        private void AppendLog_(string logType, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
               // sb.AppendLine("[" + logType + "]" + "[" + DateTime.Now.ToString() + "] : " + message);
               // LogText = sb.ToString();
               Logs.Add(new Log() { Message = message, LogType = logType});
            });
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            this.Close();
        }

        private void CrossBtnClick(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void DebugLog_TextChanged(object sender, TextChangedEventArgs e)
        {
            
        }

        private void MaximizeClicked(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Maximized;
        }

        private void MinimizeClicked(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;

        }
    }
}

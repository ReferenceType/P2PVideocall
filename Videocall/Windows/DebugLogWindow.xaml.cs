using System;
using System.Collections.Generic;
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
                    instance= new DebugLogWindow();
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
        static StringBuilder sb = new StringBuilder();

        public DebugLogWindow()
        {
            instance= this;
            var chr = new WindowChrome();
            WindowChrome.SetWindowChrome(this, chr);
            InitializeComponent();
        }

        public static void AppendLog(string logType, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                sb.AppendLine("[" + logType + "]" + "[" + DateTime.Now.ToString() + "] : " + message);
                LogText = sb.ToString();
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
    }
}

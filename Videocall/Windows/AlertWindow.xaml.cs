using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Windows.UI.Xaml.Data;

namespace Videocall
{
    /// <summary>
    /// Interaction logic for AlertWindow.xaml
    /// </summary>
    public partial class AlertWindow : Window
    {
        private static AlertWindow instance;
        public static AlertWindow Instance
        {
            get

            {
                if (instance == null)
                {
                    instance = new AlertWindow();
                }
                return instance;
            }
        }

        static TaskCompletionSource<string > userActionResult;
        public AlertWindow()
        {
           this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            InitializeComponent();
        }

        private void CloseButtonClicked(object sender, RoutedEventArgs e)
        {
            userActionResult.TrySetResult(AsyncToastNotificationHandler.CallRejected);
            this.Hide();

        }

        private void AcceptBtnClicked(object sender, RoutedEventArgs e)
        {
            userActionResult.TrySetResult(AsyncToastNotificationHandler.CallAccepted);
            this.Hide();
        }

        private void RejectBtnClicked(object sender, RoutedEventArgs e)
        {
            userActionResult.TrySetResult(AsyncToastNotificationHandler.CallRejected);
            this.Hide();

        }

        public static void CancelDialog()
        {
            userActionResult.TrySetCanceled();
            Application.Current.Dispatcher.BeginInvoke(new Action(() => Instance.Hide()));

        }

        public static async Task<string> ShowCallDialog(string callerName, int timeoutMs = 10000)
        {
            string text = "{0} is calling you would you like to aswer?";
            await Application.Current.Dispatcher.BeginInvoke(new Action(() => 
            {
            
                Instance.Show();
                instance.Activate();
                instance.Topmost= true;
                Instance.InfoText.Text = string.Format(text, callerName);
                SystemSounds.Beep.Play();
                
            }));
           

            userActionResult = new TaskCompletionSource<string>();

            if(await Task.WhenAny(userActionResult.Task, Task.Delay(timeoutMs)) == userActionResult.Task)
            {
                return userActionResult.Task.Result;
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>Instance.Hide()));
                return "timeout";
            }

        }
    }
}

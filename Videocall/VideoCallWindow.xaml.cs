
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using Videocall.Services.HttpProxy;
using Videocall.Services.Latency;
using Videocall.Settings;
using Windows.ApplicationModel;
using Windows.Media.Protection.PlayReady;

namespace Videocall
{
    /// <summary>
    /// Interaction logic for NewMainWindow.xaml
    /// </summary>
    public partial class VideoCallWindow : Window
    {
        public SettingsViewModel SettingsViewModel { get; set; }
        public MainWindowViewModel MainWindowViewModel { get; set; }

        AudioHandler AudioHandler { get; set; }

        VideoHandler VideoHandler { get; set; }

        FileShare FileShare { get; set; }

        MessageHandler MessageHandler { get; set; }
        internal LatencyPublisher LatencyPublisher { get; }

        private CameraWindow cameraWindow = new CameraWindow();
        private bool restoreForDragMove;
       // DebugLogWindow debugwindow =  new DebugLogWindow();
        public VideoCallWindow()
        {
            AudioHandler =  new AudioHandler();
            VideoHandler = new VideoHandler();
            FileShare =  new FileShare();
            MessageHandler= new MessageHandler();
            LatencyPublisher = new LatencyPublisher(MessageHandler);

            AudioHandler.StartMic();
            AudioHandler.StartSpeakers();

            ServiceHub hub = new ServiceHub(AudioHandler, VideoHandler, MessageHandler, FileShare,LatencyPublisher);

            MainWindowViewModel = new MainWindowViewModel(hub);
            MainWindowViewModel.SrollToEndChatWindow+= ()=> MessageWindow.ScrollToEnd();
            SettingsViewModel = new SettingsViewModel(hub);

            DataContext= this;
            cameraWindow.DataContext = MainWindowViewModel;

            InitializeComponent();
            var chr = new WindowChrome();
            WindowChrome.SetWindowChrome(this,chr );


            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_ProcessExit;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            Application.Current.Exit += Current_Exit;


           
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

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter &&
                !(Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) &&
                 !(Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)))
            {
                MainWindowViewModel.ChatInputText = ChatMessageBox.Text;
                MainWindowViewModel.SendTextCommand.Execute(null);
            }
        }
        private void MessageWindow_Drop(object sender, DragEventArgs e) 
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                MainWindowViewModel.HandleDrop(files);


            }
        }

        #region HardExit
        private void Current_Exit(object sender, ExitEventArgs e)
        {
            VideoHandler.CloseCamera();

        }

        private void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Environment.Exit(0);
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            VideoHandler.CloseCamera();
        }

        #endregion

        #region Custom window Code behind
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
                    this.BorderThickness = new System.Windows.Thickness(5);
                    cameraWindow.Hide();

                    break;
                case WindowState.Minimized:
                    if (CallStateManager.GetState() == CallStateManager.CallState.OnCall)
                    {
                        cameraWindow.Show();
                        if (cameraWindow.Owner == null)
                            cameraWindow.Owner = this;
                    }
                        

                    break;
                case WindowState.Normal:
                    this.BorderThickness = new System.Windows.Thickness(1);
                    cameraWindow.Hide();

                    break;
            }
        }

        private void CloseBtnClick(object sender, RoutedEventArgs e)
        {
            cameraWindow.Close();
            this.Close();

        }

        private void MaximizeBtcClicked(object sender, RoutedEventArgs e)
        {
            if (this.WindowState != WindowState.Maximized)
                this.WindowState = WindowState.Maximized;
            else
                this.WindowState = WindowState.Normal;


        }

        private void MinimizeBtnClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;

        }


        #region Drag move functionality
        /*
         *  MouseMove="Window_MouseMove"
        MouseLeftButtonDown="Window_MouseLeftButtonDown"
        MouseLeftButtonUp="Window_MouseLeftButtonUp"
         */
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (restoreForDragMove)
            {
                restoreForDragMove = false;
                var pos = e.MouseDevice.GetPosition(this);
                var point = PointToScreen(pos);

                double percantageX = pos.X / Width;
                double percantageY = pos.Y / Height;
                Left = pos.X - RestoreBounds.Width*percantageX;
                Top = pos.Y - RestoreBounds.Height*percantageY;

                WindowState = WindowState.Normal;
                DragMove();
                //DispatcherRun(async () => { await Task.Delay(1000); DragMove(); }); 
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (ResizeMode != ResizeMode.CanResize &&
                    ResizeMode != ResizeMode.CanResizeWithGrip)
                {
                    return;
                }

                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                restoreForDragMove = WindowState == WindowState.Maximized;
                DragMove();
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            restoreForDragMove = false;

        }
        #endregion

        #endregion

        private void ShowDebugWindow(object sender, RoutedEventArgs e)
        {
            if(!DebugLogWindow.Instance.IsVisible)
                DebugLogWindow.Instance.Show();
            else
                DebugLogWindow.Instance.Hide();

        }

    }

}

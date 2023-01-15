
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Protobuff;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Videocall.Services.HttpProxy;
using Videocall.Services.Latency;
using Videocall.Services.ScreenShare;
using Videocall.Settings;
using Windows.ApplicationModel;
using Windows.Media.Protection.PlayReady;
using Windows.UI.Xaml.Media.Imaging;

namespace Videocall
{
    
    public partial class VideoCallWindow : System.Windows.Window
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

        SimpleScreenShareHandler ScreenShareHandler =  new SimpleScreenShareHandler();
        private System.Windows.Forms.NotifyIcon _notifyIcon;

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

            ServiceHub hub = new ServiceHub(AudioHandler, VideoHandler, MessageHandler, FileShare,LatencyPublisher, ScreenShareHandler);

            MainWindowViewModel = new MainWindowViewModel(hub);
            MainWindowViewModel.SrollToEndChatWindow += () => {
                ChatView.SelectedIndex = ChatView.Items.Count==0?0: ChatView.Items.Count - 1;
                ChatView.ScrollIntoView (ChatView.SelectedItem);
                
            };
            SettingsViewModel = new SettingsViewModel(hub);

            DataContext= this;
            cameraWindow.DataContext = MainWindowViewModel;

            //MainWindowViewModel.MicroponeChecked = true;

            InitializeComponent();
            var chr = new WindowChrome();
            chr.ResizeBorderThickness=new Thickness(10,10,10,10);
            WindowChrome.SetWindowChrome(this,chr );


            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_ProcessExit;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            Application.Current.Exit += Current_Exit;
            Task.Run(async () => { await Task.Delay(50); DispatcherRun(() => CameraButton.IsChecked = true); });
            Task.Run(async () => { await Task.Delay(50); DispatcherRun(() => SoundButton.IsChecked = true); });
            ScreenShareHandler.ImageAvailable += (bytes,mat) =>
            {
                var env = new MessageEnvelope();
                env.Header = "ScreenShareImage";
                env.Payload = bytes;
                MessageHandler.client.SendAsyncMessage(CallStateManager.GetCallerId(), env);

                DispatcherRun(()=> MainWindowViewModel.SecondaryCanvasSource = mat.ToBitmapSource());

            };

            MessageHandler.OnMessageAvailable += (message) =>
            {
                if (message.Header == "ScreenShareImage")
                {
                    ScreenShareHandler.HandleNetworkImageBytes(message.Payload);
                }
            };

            ScreenShareHandler.ImageReceived += (mat) =>
            {
                DispatcherRun(() =>
                {
                    MainWindowViewModel.PrimaryCanvasSource = mat.ToBitmapSource();

                });
            };

            Task.Run(async() => { await Task.Delay(1000); scrollActive = true; });
            CallStateManager.StaticPropertyChanged += OnCallStateChanged;
            ChatHideButton.Visibility = Visibility.Hidden;
            PeersHideButton.Visibility = Visibility.Hidden;
            CamGridColumn.Width = new GridLength(0, GridUnitType.Star);
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
                   // cameraWindow.Hide();

                    break;
                case WindowState.Minimized:
                    if (CallStateManager.GetState() == CallStateManager.CallState.OnCall)
                    {
                        //cameraWindow.Show();
                        //if (cameraWindow.Owner == null)
                        //    cameraWindow.Owner = this;
                    }
                        

                    break;
                case WindowState.Normal:
                    this.BorderThickness = new System.Windows.Thickness(1);
                   // cameraWindow.Hide();

                    break;
            }
        }

        private void CloseBtnClick(object sender, RoutedEventArgs e)
        {
            cameraWindow.Hide();
            this.Hide();

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

       

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            string uriStr = e.Uri.ToString();
            if (!uriStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase) 
                && !uriStr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                uriStr = "http://" + uriStr;

            var sInfo = new System.Diagnostics.ProcessStartInfo(uriStr)
            {
                UseShellExecute = true,
            };
            try
            {
                Process.Start(sInfo);
                this.Topmost = false;

            }
            catch { }
        }

        private void ChatMessageBox_GotFocus(object sender, RoutedEventArgs e)
        {
            WriteMsgGhostText.Opacity = 0;
        }

        private void ChatMessageBox_LostFocus(object sender, RoutedEventArgs e)
        {
            WriteMsgGhostText.Opacity = 1;

        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            MainWindowViewModel.WindowsActive = false;

            if (CallStateManager.GetState() != CallStateManager.CallState.OnCall) return;

            cameraWindow.Show();
            if (cameraWindow.Owner == null)
                cameraWindow.Owner = this;

        }

        private void Window_Activated(object sender, EventArgs e)
        {
            MainWindowViewModel.WindowsActive = true;

            cameraWindow.Hide();
        }

        private bool scrollActive;

        // pain train
        private void ChatView_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            if(!scrollActive) return;

            var off = e.VerticalOffset;
            if (off == 0 )
            {
                scrollActive = false;
                Task.Run(async () => { await Task.Delay(600); scrollActive = true; });

                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    DispatcherRun(() =>
                    {
                        if (MainWindowViewModel.LoadMoreMessages() && ChatView.Items.Count > 20)
                        {
                            ChatView.ScrollIntoView(ChatView.SelectedItem);
                            for (int i = 10; i < 20; i++)
                            {
                                ChatView.SelectedIndex = i;
                                DispatcherRun(() => ChatView.ScrollIntoView(ChatView.SelectedItem));

                            }

                        }
                    });
                });
            }
            
           
        }

        private void OnCallStateChanged(object sender, PropertyChangedEventArgs e)
        {
            DispatcherRun(() =>
            {
                if (CallStateManager.GetState() == CallStateManager.CallState.Calling || CallStateManager.GetState() == CallStateManager.CallState.OnCall)
                {
                    CamGridColumn.Width = new GridLength(5,GridUnitType.Star);
                    //ChatGridColumn.Width = new GridLength(1.72,GridUnitType.Star);
                    ChatGridColumn.Width = new GridLength(280,GridUnitType.Pixel);
                    PeersGridColumn.Width = new GridLength(200, GridUnitType.Pixel);

                    chatViewState = 0;
                    ChatHideButton.Content = ">>";
                    ChatHideButton.Visibility = Visibility.Visible;
                    PeersHideButton.Visibility = Visibility.Visible;
                    ChatGridColumn.MinWidth = 0;
                    PeersGridColumn.MinWidth = 0;


                }
                else
                {
                    CamGridColumn.Width = new GridLength(0, GridUnitType.Pixel);
                    ChatGridColumn.Width = new GridLength(1, GridUnitType.Star);
                    PeersGridColumn.Width = new GridLength(200, GridUnitType.Pixel);
                    ChatHideButton.Visibility = Visibility.Hidden;
                    PeersHideButton.Visibility = Visibility.Hidden;
                    ChatGridColumn.MinWidth = 280;
                    PeersGridColumn.MinWidth = 200;
                }



            });
        }

        int chatViewState= 0;
        int peersViewState= 0;

        

        private void ChatHideButton_Click(object sender, RoutedEventArgs e)
        {
            switch (chatViewState)
            {
                case 0:
                    ChatGridColumn.MinWidth=0;
                    ChatGridColumn.Width = new GridLength(0, GridUnitType.Pixel);
                    CamGridColumn.Width = new GridLength(1, GridUnitType.Star);
                    chatViewState= 1;
                    ChatHideButton.Content= "<<";
                 break;

                 case 1:
                    ChatGridColumn.Width = new GridLength(1, GridUnitType.Star);

                    if (CallStateManager.GetState() == CallStateManager.CallState.Calling || CallStateManager.GetState() == CallStateManager.CallState.OnCall)
                    {
                        CamGridColumn.Width = new GridLength(1, GridUnitType.Star);
                        if(peersViewState==1)
                            ChatGridColumn.Width = new GridLength(480, GridUnitType.Pixel);
                        else
                            ChatGridColumn.Width = new GridLength(280, GridUnitType.Pixel);



                    }
                    else
                    {
                        ChatGridColumn.Width = new GridLength(1, GridUnitType.Star);
                        CamGridColumn.Width = new GridLength(0, GridUnitType.Star);

                    }

                    chatViewState = 0;
                    ChatHideButton.Content = ">>";

                    break;

            }
          
        }

        private void PeersHideButton_Click(object sender, RoutedEventArgs e)
        {

            switch (peersViewState)
            {
                case 0:
                    PeersGridColumn.Width = new GridLength(0, GridUnitType.Pixel);
                    CamGridColumn.Width = new GridLength(2, GridUnitType.Star);
                    if(chatViewState==0)
                        ChatGridColumn.Width = new GridLength(480, GridUnitType.Pixel);


                    peersViewState = 1;
                    PeersHideButton.Content = ">>";

                    break;

                case 1:

                    PeersGridColumn.Width = new GridLength(200, GridUnitType.Pixel);
                    CamGridColumn.Width = new GridLength(1, GridUnitType.Star);
                    if (chatViewState == 0)
                        ChatGridColumn.Width = new GridLength(280, GridUnitType.Pixel);

                    PeersHideButton.Content = "<<";

                    peersViewState = 0;
                    break;

            }

            
        }

      
    }

}

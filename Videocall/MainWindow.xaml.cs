using NetworkLibrary.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Protobuff;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using Videocall.Services.HttpProxy;
using Videocall.Services.Latency;
using Videocall.Services.ScreenShare;
using Videocall.Settings;


namespace Videocall
{
    
    public partial class MainWindow : System.Windows.Window
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
        public MainWindow()
        {
            InitializeComponent();

            AudioHandler =  new AudioHandler();
            VideoHandler = new VideoHandler();
            FileShare =  new FileShare();
            MessageHandler= new MessageHandler();
            LatencyPublisher = new LatencyPublisher(MessageHandler);
            MiniLogger.AllLog += (string s) => DebugLogWindow.AppendLog("any",s);
            //AudioHandler.StartMic();
            AudioHandler.StartSpeakers();

            ServiceHub hub = new ServiceHub(AudioHandler, VideoHandler, MessageHandler, FileShare,LatencyPublisher, ScreenShareHandler);

            MainWindowViewModel = new MainWindowViewModel(hub);
            MainWindowViewModel.SrollToEndChatWindow += () => {
                ChatView.SelectedIndex = ChatView.Items.Count == 0?0: ChatView.Items.Count - 1;
                ChatView.ScrollIntoView(ChatView.SelectedItem);
                
            };
            SettingsViewModel = new SettingsViewModel(hub);

            DataContext= this;
            cameraWindow.DataContext = MainWindowViewModel;

            //MainWindowViewModel.MicroponeChecked = true;
            HandleScreenshare();

            var chr = new WindowChrome();
            chr.ResizeBorderThickness=new Thickness(10,10,10,10);
            WindowChrome.SetWindowChrome(this,chr );

            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_ProcessExit;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            Application.Current.Exit += Current_Exit;

            Task.Run(async () => { await Task.Delay(50); DispatcherRun(() => CameraButton.IsChecked = true); });
            Task.Run(async () => { await Task.Delay(50); DispatcherRun(() => SoundButton.IsChecked = true); });

         
            Task.Run(async() => { await Task.Delay(1000); scrollActive = true; });
            CallStateManager.StaticPropertyChanged += OnCallStateChanged;
            ChatHideButton.Visibility = Visibility.Hidden;
            PeersHideButton.Visibility = Visibility.Hidden;
            CamGridColumn.Width = new GridLength(0, GridUnitType.Star);
        }

        private void HandleScreenshare()
        {
            ScreenShareHandler.LocalImageAvailable += (bytes, image) =>
            {
                var env = new MessageEnvelope();
                env.Header = "ScreenShareImage";
                env.Payload = bytes;
                MessageHandler.client.SendAsyncMessage(CallStateManager.GetCallerId(), env);

                DispatcherRun(() => {

                    if (MainWindowViewModel.SecondaryCanvasSource == null
                      || image.Width != MainWindowViewModel.SecondaryCanvasSource.Width
                      || image.Height != MainWindowViewModel.SecondaryCanvasSource.Height)
                        MainWindowViewModel.SecondaryCanvasSource = image.ToBitmapSource();
                    else
                    {
                        var dst = (System.Windows.Media.Imaging.WriteableBitmap)MainWindowViewModel.SecondaryCanvasSource;
                        dst.Lock();
                        int width = image.Width;
                        int height = image.Height;
                        int step = (int)image.Step();
                        long range = image.DataEnd.ToInt64() - image.Data.ToInt64();

                        dst.WritePixels(new Int32Rect(0, 0, width, height), image.Data, (int)range, step);
                        dst.Unlock();
                        image.Dispose();
                    }
                });

            };

            MessageHandler.OnMessageAvailable += (message) =>
            {
                if (message.Header == "ScreenShareImage")
                {
                    ScreenShareHandler.HandleNetworkImageBytes(message.Payload, message.PayloadOffset, message.PayloadCount);
                }
            };

            ScreenShareHandler.RemoteImageAvailable += (image) =>
            {
                DispatcherRun(() =>
                {
                    if (MainWindowViewModel.PrimaryCanvasSource == null
                    || image.Width != MainWindowViewModel.PrimaryCanvasSource.Width
                    || image.Height != MainWindowViewModel.PrimaryCanvasSource.Height)
                        MainWindowViewModel.PrimaryCanvasSource = image.ToBitmapSource();
                    else
                    {
                        var dst = (System.Windows.Media.Imaging.WriteableBitmap)MainWindowViewModel.PrimaryCanvasSource;
                        dst.Lock();
                        int width = image.Width;
                        int height = image.Height;
                        int step = (int)image.Step();
                        long range = image.DataEnd.ToInt64() - image.Data.ToInt64();

                        dst.WritePixels(new Int32Rect(0, 0, width, height), image.Data, (int)range, step);
                        dst.Unlock();
                        image.Dispose();
                    }

                });
            };
        }

        private void DispatcherRun(Action todo)
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(todo);

            }
            catch(Exception e )
            {
                DebugLogWindow.AppendLog("Error Dispatcher", e.Message);
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
                    break;
                case WindowState.Normal:
                    this.BorderThickness = new System.Windows.Thickness(1);
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

        #endregion

        private void ShowDebugWindow(object sender, RoutedEventArgs e)
        {
            if(!DebugLogWindow.Instance.IsVisible)
                DebugLogWindow.Instance.Show();
            else
                DebugLogWindow.Instance.Hide();

        }

        private void OnHyperlinkClicked(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
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


        #region Chat window 
        private void ChatMessageBox_GotFocus(object sender, RoutedEventArgs e)
        {
            WriteMsgGhostText.Opacity = 0;
        }

        private void ChatMessageBox_LostFocus(object sender, RoutedEventArgs e)
        {
            WriteMsgGhostText.Opacity = 1;

        }

        // pain train, this autoloads the historical chat as you scroll up.
        private bool scrollActive;
        private void ChatView_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            if(!scrollActive) return;

            var off = e.VerticalOffset;
            if(off==0 && e.VerticalChange == 0 )
            {
                return;
            }
            if (off == 0)
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
        #endregion

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
                    ScreenShareHandler.StopCapture();
                }
            });
        }




        #region Window hide expand region logic

        int chatViewState = 0;
        int peersViewState = 0;
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
                        ChatGridColumn.Width = new GridLength(280, GridUnitType.Pixel);


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
        #endregion
        private void Status_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            Status.ScrollToEnd();
        }
    }

}

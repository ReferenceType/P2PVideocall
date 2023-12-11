using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Videocall
{
    /// <summary>
    /// Interaction logic for CameraWindow.xaml
    /// </summary>
    public partial class CameraWindow : Window
    {
        public CameraWindow()
        {
            InitializeComponent();
            CallStateManager.Instance.StaticPropertyChanged += CallHandlerStateChanged;
            var chr = new WindowChrome();
            //chr.GlassFrameThickness =  new Thickness(1,1,1,1);
            chr.ResizeBorderThickness = new Thickness(10, 10, 10, 10);

            WindowChrome.SetWindowChrome(this, chr);
        }

        private void CallHandlerStateChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (CallStateManager.GetState() == CallStateManager.CallState.Available)
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>this.Hide())) ;
        }

        private void CloseWindowBtnClick(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if(e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if(this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;

            }
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width;
            this.Top = desktopWorkingArea.Top /*+ this.Height*/;
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            this.Hide();
            Owner.WindowState = WindowState.Normal;
            App.ShowMainWindow();
        }

        private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            
        }
    }
}

using System;
using System.Collections.ObjectModel;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Videocall;


public class MainWindowViewModel : PropertyNotifyBase
{
    private string chatText;
    private string chatInputText;
    private string fTProgressText;
    private bool cameraChecked;
    private bool microponeChecked = false;
    private bool endCallVisibility = false;
    private double windowWidth=800;
    private double windowHeight=550;


    public Action SrollToEndChatWindow;

    public ICommand CallSelectedCommand { get; }
    public ICommand EndCallCommand { get; }
    public ICommand SendTextCommand { get; }


    private ImageSource primaryCanvasSource;
    public ImageSource PrimaryCanvasSource { get => primaryCanvasSource;
        set { primaryCanvasSource = value; OnPropertyChanged(); } }

    private ImageSource secondaryCanvasSource;
    public ImageSource SecondaryCanvasSource { get => secondaryCanvasSource;
        set { secondaryCanvasSource = value; OnPropertyChanged(); } }


    private double canvasColumn=2;
    public double CanvasColumn { get => canvasColumn; set { canvasColumn = value; OnPropertyChanged(); } }

    private double chatColumn = -1;
    public double ChatColumn { get => chatColumn; set { chatColumn = value; OnPropertyChanged(); } }

    public string ChatText { get => chatText; set { chatText = value; OnPropertyChanged(); } }
    public string ChatInputText { get => chatInputText; set { chatInputText = value; OnPropertyChanged(); } }

    public string FTProgressText { get => fTProgressText; set { fTProgressText = value; OnPropertyChanged(); } }
    public bool CameraChecked { get => cameraChecked; set { cameraChecked = value; HandleCamChecked(value); OnPropertyChanged(); } }

  
    public bool MicroponeChecked { get => microponeChecked; set { microponeChecked = value; HandleMicChecked(value); OnPropertyChanged(); } }

    
    public ObservableCollection<PeerInfo> PeerInfos { get; set; } = new ObservableCollection<PeerInfo>();

    private PeerInfo selectedPeer = null;
    public PeerInfo SelectedItem { get => selectedPeer; set { selectedPeer = value; OnPropertyChanged(); } }

    public bool EndCallVisibility { get => endCallVisibility; set { endCallVisibility = value; OnPropertyChanged(); } }

    public double WindowWidth { get => windowWidth; set { windowWidth = value; OnPropertyChanged(); } }
    public double WindowHeight { get => windowHeight; set { windowHeight = value; OnPropertyChanged(); } }

    private ServiceHub services;
    private MainWindowModel model;
    internal MainWindowViewModel(ServiceHub services)
    {
        this.services = services;
        CallSelectedCommand = new RelayCommand(HandleCallSelected);
        EndCallCommand = new RelayCommand(HandleEndCallClicked);
        SendTextCommand = new RelayCommand(HandleSendText);
        model = new MainWindowModel(this,services);


    }
    private void HandleCamChecked(bool value)
    {
       
        model.HandleCamActivated(value);

    }

    private void HandleMicChecked(bool value)
    {
        model.HandleMicChecked(value);
    }


    private void HandleSendText(object obj)
    {
        DispatcherRun(() =>
        {
            model.HandleUserChatSend(ChatInputText);
            ChatText += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + "You" + " : " + ChatInputText;
            ChatInputText = "";
            SrollToEndChatWindow?.Invoke();
        }); 
    }

    public void WriteChatEntry(string sender,string message)
    {
        DispatcherRun(() =>
       ChatText += "\n" + "[" + DateTime.Now.ToShortTimeString() + "] " + sender + " : " + message);
    }

    private void HandleEndCallClicked(object obj)
    {
        if(selectedPeer!=null)
             model.HandleEndCallUserRequest();
    }

    private void HandleCallSelected(object obj)
    {
        if(selectedPeer != null)
        model.HandleCallUserRequest(selectedPeer);

        
    }

    public void HandleDrop(string[] files)
    {
        if(selectedPeer!=null)
            model.HandleFileDrop(files, selectedPeer.Guid);
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

}


using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Videocall;
using Videocall.Models;
using Videocall.Models.MainWindow;

public class MainWindowViewModel : PropertyNotifyBase
{
    private string chatText;
    private string chatInputText;
    private string fTProgressText= FTProgressTextDefault;
    private const string defaultSelectedPeerTxt = "Write message..";
    public const string FTProgressTextDefault = "Drag & Drop File or Folder to Chat to Send..";
    private string selectedPeerText= "Write message..";

    private bool cameraChecked;
    private bool shareScreenChecked;
    private bool microponeChecked = false;
    private bool endCallVisibility = false;
    private double windowWidth=1330;
    private double windowHeight=800;
    private int fTProgressPercent;


    public Action SrollToEndChatWindow;

    public ICommand CallSelectedCommand { get; }
    public ICommand EndCallCommand { get; }
    public ICommand SendTextCommand { get; }
    public ICommand FTCancelCmd { get; }


    private ImageSource primaryCanvasSource;
    public ImageSource PrimaryCanvasSource { get => primaryCanvasSource;
        set { primaryCanvasSource = value; OnPropertyChanged(); } }

    private ImageSource secondaryCanvasSource;
    public ImageSource SecondaryCanvasSource { get => secondaryCanvasSource;
        set { secondaryCanvasSource = value; OnPropertyChanged(); } }


    private double canvasColumn=2;
    public double CanvasColumn { get => canvasColumn; 
        set { canvasColumn = value; OnPropertyChanged(); } }

    private double chatColumn = -1;
    public double ChatColumn { get => chatColumn; 
        set { chatColumn = value; OnPropertyChanged(); } }

    public string ChatText { get => chatText; 
        set { chatText = value; OnPropertyChanged(); } }
    public string ChatInputText { get => chatInputText; 
        set { chatInputText = value; OnPropertyChanged(); } }

    public string FTProgressText { get => fTProgressText; 
        set { fTProgressText = value; OnPropertyChanged(); } }
    public bool CameraChecked { get => cameraChecked; 
        set { cameraChecked = value; HandleCamChecked(value); OnPropertyChanged(); } }

    public bool MicroponeChecked { get => microponeChecked; 
        set { microponeChecked = value; HandleMicChecked(value); OnPropertyChanged(); } }

    public ObservableCollection<PeerInfo> PeerInfos { get; set; }
        = new ObservableCollection<PeerInfo>();

    public ObservableCollection<ChatDataModel> ChatData { get; set; } 
        = new ObservableCollection<ChatDataModel>();

    private PeerInfo selectedPeer = null;
    public PeerInfo SelectedItem { get => selectedPeer; 
        set { selectedPeer = value; SelectedPeerChanged(value); OnPropertyChanged(); } }

    private void SelectedPeerChanged(PeerInfo value)
    {
        if(value == null)
        {
            SelectedPeerText = defaultSelectedPeerTxt;
        }
        else
            SelectedPeerText = $"Write message to {value.Name}..";
    }
    public string SelectedPeerText { get => selectedPeerText; set { selectedPeerText = value; OnPropertyChanged(); } }

    public bool EndCallVisibility { get => endCallVisibility; 
        set { endCallVisibility = value; OnPropertyChanged(); } }

    public double WindowWidth { get => windowWidth; 
        set { windowWidth = value; OnPropertyChanged(); } }
    public double WindowHeight { get => windowHeight; 
        set { windowHeight = value; OnPropertyChanged(); } }

    public bool ShareScreenChecked { get => shareScreenChecked; 
        set { shareScreenChecked = value; HandleShareScreenChecked(value); OnPropertyChanged(); } }
    private string callStatus = "Available";
    public bool WindowsActive { get; internal set; }
    public string CallStatus { get => callStatus; set{ callStatus = value; OnPropertyChanged(); } }

    public int FTProgressPercent { get => fTProgressPercent; set { fTProgressPercent = value; OnPropertyChanged(); } }

    public bool FTCancelBtnVisibility { get => fTCancelBtnVisibility; set { fTCancelBtnVisibility = value; OnPropertyChanged(); } }

    public string FTProgressRatio { get => fTProgressRatio; set { fTProgressRatio = value; OnPropertyChanged(); } }


    private string fTProgressRatio;
    private bool fTCancelBtnVisibility = false;
    private ServiceHub services;
    private MainWindowModel model;

    private ChatSerializer chatSerializer;
    public Action OnFtCancelled;
    internal MainWindowViewModel()
    {
        services = ServiceHub.Instance;

        CallSelectedCommand = new RelayCommand(HandleCallSelected);
        EndCallCommand = new RelayCommand(HandleEndCallClicked);
        SendTextCommand = new RelayCommand(HandleChatSend);
        FTCancelCmd = new RelayCommand(HandleFTCancelCmd);
        model = new MainWindowModel(this,services);

        chatSerializer = new ChatSerializer(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
        LoadMoreMessages();
        DispatcherRun(()=> SrollToEndChatWindow?.Invoke());
        MainWindowEventAggregator.Instance.ClearChatHistoryRequested += ClearChatHistory;
        CallStateManager.Instance.StaticPropertyChanged += (ignore, v) => { Application.Current.Dispatcher.Invoke(() => CallStatus = CallStateManager.Instance.CurrentState); };
    }

    private void HandleFTCancelCmd(object obj)
    {
        OnFtCancelled?.Invoke();
    }

    private void ClearChatHistory()
    {
        chatSerializer.ClearAllHistory();
        ChatData.Clear();
    }

    public bool LoadMoreMessages()
    {
        //collection.Insert(0, item);
        bool succes = chatSerializer.LoadFromEnd(20, out var messages);
        if (!succes) return succes;
        foreach (var item in messages)
        {
            switch (item.MessageType)
            {
                case ChatSerializationData.MsgType.Local:
                    var chat = WriteLocalChat(item.Message, item.TimeStamp);
                    chat.Sender = item.Sender;
                    ChatData.Insert(0, chat);
                    //SrollToEndChatWindow?.Invoke();

                    break;
                case ChatSerializationData.MsgType.Remote:
                    var msg = CreateRemoteChatItem(item.Sender, item.Message);
                    msg.Time = item.TimeStamp.ToShortTimeString();
                    msg.Sender= item.Sender;
                    ChatData.Insert(0, msg);

                    break;
                case ChatSerializationData.MsgType.Info:
                    break;
            }
        }
        return succes;
    }

    private void HandleCamChecked(bool value)
    {
        model.HandleCamActivated(value);
    }
    private void HandleShareScreenChecked(bool value)
    {
        model.HandleShareScreenChecked(value);
    }

    private void HandleMicChecked(bool value)
    {
        model.HandleMicChecked(value);
    }


    private void HandleChatSend(object obj)
    {

        DispatcherRun(() =>
        {

            var chatData = WriteLocalChat(ChatInputText, DateTime.Now);
            chatSerializer.SerializeLocalEntry(ChatInputText, DateTime.Now, chatData.Sender);
            model.HandleUserChatSend(ChatInputText);


            ChatData.Add(chatData);

            ChatInputText = "";
            SrollToEndChatWindow?.Invoke();
        }); 
    }

    private ChatDataModel WriteLocalChat(string message, DateTime timeStamp)
    {
        ChatDataModel chatData = new ChatDataModel();
        chatData.CreateLocalChatEntry(message,timeStamp);
        if (ChatData.Count > 0 && ChatData.Last().Allignment == "Right"
        && (ChatData.Last().Sender == "You" || ChatData.Last().Sender == null))
        {
            chatData.Sender = null;
        }
        return chatData;
    }
   
    public void WriteRemoteChat(string sender,string message)
    {
        DispatcherRun(() => {
            if (!WindowsActive)
            {
                AsyncToastNotificationHandler.ShowInfoNotification(sender + " sent a message :\n" + message);

            }
            var chatData = CreateRemoteChatItem(sender, message);
            chatSerializer.SerializeRemoteEntry(chatData.Sender,message,DateTime.Now);
            ChatData.Add(chatData);
            SrollToEndChatWindow?.Invoke();

        });

    }
    private ChatDataModel CreateRemoteChatItem(string sender, string message)
    {
        ChatDataModel chatData = new ChatDataModel();

        chatData.CreateRemoteChatEntry(sender, message);
        if (ChatData.Count > 0 && ChatData.Last().Allignment == "Left" &&
        (ChatData.Last().Sender == sender || ChatData.Last().Sender == null))
        {
            chatData.Sender = null;
        }
        return chatData;
    }
    public void WriteInfoEntry(string entry, string url = null)
    {
        DispatcherRun(() => {
            ChatDataModel chatData = new ChatDataModel();
            chatData.CreateInfoChatEntry( entry, url);
            ChatData.Add(chatData);
            SrollToEndChatWindow?.Invoke();

        });
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


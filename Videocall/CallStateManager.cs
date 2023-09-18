using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace Videocall
{
    internal class CallStateManager 
    {
        private static CallStateManager instance;
        internal static CallStateManager Instance { 
            get
            {
                if(instance == null)
                {
                    instance = new CallStateManager();
                }
                return instance;
            }
        }

        public event EventHandler<PropertyChangedEventArgs> StaticPropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(name));
        }

        public enum CallState
        {
            Available,
            Calling,
            ReceivingCall,
            OnCall,
        }
        CallState cs = CallState.Available;
        CallState currentState { get => Instance.cs; set 
            {
                cs= value;
                CurrentState = Enum.GetName(typeof(CallState), cs);
            } }

        public string CurrentState { get => currentState1; set { currentState1 = value; OnPropertyChanged(); } }

        private static string currentState1 = "Available";
        Guid CallingWith;


        public static void RegisterCall(Guid callingWith)
        {
            Instance.CallingWith = callingWith;
            Instance.currentState = CallState.OnCall;
            //AsyncToastNotificationHandler.ShowInfoNotification("You are on call",3000);


        }

        public static void UnregisterCall(Guid callingWith)
        {
            if(callingWith == Instance.CallingWith)
            {
                Instance.currentState = CallState.Available;
               // AsyncToastNotificationHandler.ShowInfoNotification("Call Ended", 3000);
            }
                

        }

        public static void EndCall()
        {
            if (Instance.currentState != CallState.Available)
            {
                Instance.currentState = CallState.Available;
                //AsyncToastNotificationHandler.ShowInfoNotification("Call Ended");
            }
            

        }

        public static void Calling()
        {
            if (Instance.currentState == CallState.Available)
            {
                Instance.currentState = CallState.Calling;

            }
        }

        public static void ReceivingCall()
        {
            if (Instance.currentState == CallState.Available)
            {
                Instance.currentState = CallState.ReceivingCall;

            }
        }
        public static bool CanReceiveCall() => Instance.currentState == CallState.Available;
        public static bool CanSendCall() => Instance.currentState == CallState.Available;
        public static CallState GetState() => CallStateManager.Instance.currentState;
        public static Guid GetCallerId() => Instance.CallingWith;
        public static bool IsOnACall =>Instance.currentState == CallState.OnCall;


        internal static void CallRejected() => Instance.currentState = CallState.Available;
    }
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace Videocall
{
    internal class CallStateManager 
    {
        public static event EventHandler<PropertyChangedEventArgs> StaticPropertyChanged;
        protected static void OnPropertyChanged([CallerMemberName] string name = null)
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
        static CallState cs = CallState.Available;
        static CallState currentState { get => cs; set 
            {
                cs= value;
                CurrentState = Enum.GetName(typeof(CallState), cs);
            } }

        public static string CurrentState { get => currentState1; set { currentState1 = value; OnPropertyChanged(); } }

        private static string currentState1 = "Available";
        static Guid CallingWith;


        public static void RegisterCall(Guid callingWith)
        {
            CallingWith = callingWith;
            currentState= CallState.OnCall;
            //AsyncToastNotificationHandler.ShowInfoNotification("You are on call",3000);


        }

        public static void UnregisterCall(Guid callingWith)
        {
            if(callingWith == CallingWith)
            {
                currentState = CallState.Available;
               // AsyncToastNotificationHandler.ShowInfoNotification("Call Ended", 3000);
            }
                

        }

        public static void EndCall()
        {
            if (currentState != CallState.Available)
            {
                currentState = CallState.Available;
                //AsyncToastNotificationHandler.ShowInfoNotification("Call Ended");
            }
            

        }

        public static void Calling()
        {
            if (currentState == CallState.Available)
            {
                currentState = CallState.Calling;

            }
        }

        public static void ReceivingCall()
        {
            if (currentState == CallState.Available)
            {
                currentState = CallState.ReceivingCall;

            }
        }
        public static bool CanReceiveCall() => currentState == CallState.Available;
        public static bool CanSendCall() => currentState == CallState.Available;
        public static CallState GetState() => CallStateManager.currentState;
        public static Guid GetCallerId() => CallingWith;
        public static bool IsOnACall =>currentState == CallState.OnCall;
        internal static void CallRejected() => currentState = CallState.Available;
    }
}

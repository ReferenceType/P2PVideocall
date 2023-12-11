using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Windows.UI.Notifications;

namespace Videocall
{
    internal class AsyncToastNotificationHandler
    {
        public const string CallAccepted = "accept";
        public const string CallRejected = "reject";
        public const string UserDoubleClicked = "doubleClicked";
        public const string NotificationTimeout = "timeout";

        private static ConcurrentDictionary<string, TaskCompletionSource<string>> awaitingTasks
            = new ConcurrentDictionary<string, TaskCompletionSource<string>>();
        static AsyncToastNotificationHandler()
        {
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                HandleUserInput(toastArgs);
            };
        }

        private static async Task<string> RegisterWait(string notificationId, int timeoutMs)
        {
            awaitingTasks.TryAdd(notificationId, new TaskCompletionSource<string>());
            var pending = awaitingTasks[notificationId].Task;
            //var result = Task.WhenAny(pending, Task.Delay(timeout));

            string result = "";
            if (await Task.WhenAny(pending, Task.Delay(timeoutMs)) == pending)
            {
                // Task completed within timeout.
                result = pending.Result;
            }
            else
            {
                // timeout/cancellation logic
                result = "timeout";
                try
                {
                    // only on uwp so far..
                   // ToastNotificationManager.History.Remove(notificationId);

                }
                catch(Exception e)
                {

                }
            }

            awaitingTasks.TryRemove(notificationId, out _);
            return result;
        }
        //action=accept;notificationId=60cc3e4f-500c-42fc-8e88-ca8279c7d3cf
        private static void HandleUserInput(ToastNotificationActivatedEventArgsCompat toastArgs)
        {
            App.ShowMainWindow();
            Console.WriteLine("input");
            //w.WtireTextOnChatWindow("got input");

            var action = toastArgs.Argument.Split(';')[0];
            var id = toastArgs.Argument.Split(';')[1];

            var actionName = action.Split('=')[1];
            var actionId = id.Split('=')[1];

            ActionComplete(actionId, actionName);
        }
        private static void ActionComplete(string notificationId, string result)
        {
            if (awaitingTasks.TryGetValue(notificationId, out var completionSource))
                completionSource.SetResult(result);
        }

        public static async Task<string> ShowCallNotification(string callerName="Someone", int timeout = 10000)
        {
            var notificationId = Guid.NewGuid().ToString();
            new ToastContentBuilder()
                .AddArgument("action", "doubleClicked")
                .AddArgument("notificationId", notificationId)
                .AddText(callerName + " Wants to call you")
                .AddText("Would you like to accept the call?")
                .AddButton(new ToastButton().SetContent("Accept Call").AddArgument("action", "accept"))
                .AddButton(new ToastButton().SetContent("Reject Call").AddArgument("action", "reject"))
                .Show(toast =>
                {
                    toast.Tag = notificationId;
                });
            return await RegisterWait(notificationId, timeout);
            //return await Task.FromResult("");
        }

        public static async Task<string> ShowInfoNotification(string notificationString, int timeout = 5000)
        {
            var notificationId = Guid.NewGuid().ToString();
            try
            {
                new ToastContentBuilder()
               .AddArgument("action", "doubleClicked")
               .AddArgument("notificationId", notificationId)
               .AddText(notificationString)
               .Show(toast =>
               {
                   toast.Tag = notificationId;
                   toast.Group = "1";
                   //toast.ExpirationTime = DateTimeOffset.Now + TimeSpan.FromMilliseconds(3000);

               });
            }
            catch (Exception e)
            {
            }
           
            return await RegisterWait(notificationId, timeout);
        }
    }
}

using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace Videocall.Models.MainWindow
{
    public class ChatDataModel
    {

        public string Sender { get; set; }
        public string Message { get; set; }
        public string Time { get; set; }
        public string Allignment { get; set; } = "Left";
        public string URI { get; set; } = "";

        public double URIHeight { get => string.IsNullOrEmpty(URI) ? 0 : -1; }

        public System.Windows.Media.Color BackgroundColor { get; set; } = System.Windows.Media.Color.FromRgb(30, 30, 30);
        public Thickness RectMargin { get => string.IsNullOrEmpty(Sender) ? new Thickness(0, 0, 0, 0) : new Thickness(-3, 20, -3, 0); }

        public double SenderTextHeight { get => string.IsNullOrEmpty(Sender) ? 0 : 20; }

        public void CreateRemoteChatEntry(string sender, string message)
        {
            if (IsValidURL(message))
            {
                if (!message.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !message.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    message = "http://" + message;
                URI = message;
            }
            else
            {
                Message = message;

            }
            Sender = sender;
            Time = DateTime.Now.ToShortTimeString();
            Allignment = "Left";
            BackgroundColor = System.Windows.Media.Color.FromRgb(30, 30, 30);

        }

        public void CreateLocalChatEntry(string message, DateTime timestamp)
        {
            if (IsValidURL(message))
            {
                if (!message.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !message.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    message = "http://" + message;
                URI = message;
            }
            else
            {
                Message = message;

            }
            Sender = "You";
            Time = timestamp.ToShortTimeString();
            Allignment = "Right";
            BackgroundColor = System.Windows.Media.Color.FromRgb(35, 35, 42);
        }

        public void CreateInfoChatEntry(string info,string url = null)
        {
            URI = url;
          
            Message = info;
            Time = DateTime.Now.ToShortTimeString();
            Allignment = "Center";
            BackgroundColor = System.Windows.Media.Color.FromRgb(50, 50, 50);
        }

        const string Pattern = @"^(?:http(s)?:\/\/)?[\w.-]+(?:\.[\w\.-]+)+[\w\-\._~:/?#[\]@!\$&'\(\)\*\+,;=.]+$";
        readonly Regex Rgx = new Regex(Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        bool IsValidURL(string URL)
        {
            if (string.IsNullOrEmpty(URL)) return false;
            return Rgx.IsMatch(URL);
        }
    }
}

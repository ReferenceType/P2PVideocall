using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Videocall.Settings;

namespace Videocall.Models
{
    internal class MainWindowEventAggregator
    {
        private static MainWindowEventAggregator instance;

        public static MainWindowEventAggregator Instance 
        {
            get
            {
                if (instance == null)
                    instance = new MainWindowEventAggregator();
                return instance;
            }
        }

        public event Action ClearChatHistoryRequested;

        public void InvokeClearChatEvent()
        {
            ClearChatHistoryRequested?.Invoke();
        }
    }
}

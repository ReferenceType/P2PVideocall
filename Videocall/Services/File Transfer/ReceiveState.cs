//using NetworkLibrary;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading;
//using System.Data;

//namespace Videocall.Services.File_Transfer
//{
//    class ReceiveState : IFileTransferState
//    {
//        public int Progress => (int)(100 * ((double)TotalReceived / (double)TotalSize));

//        public Guid AssociatedPeer { get; private set; }
//        public Guid StateId { get; private set; }
//        public Action<TransferStatus> OnProgress;
//        public Action<Completion> OnCompleted;
//        public Action<Completion> Cancelled;
//        private Stopwatch sw;
//        private FileDirectoryStructure fileTree;
//        private long TotalReceived;
//        public long TotalSize { get; private set; } = 1;

//        private bool forceTCP => services.MessageHandler.FTTransportLayer == "Tcp";

//        private ServiceHub services => ServiceHub.Instance;

//        public ReceiveState(MessageEnvelope fileDirectoryMessage) 
//        {
//            AssociatedPeer = fileDirectoryMessage.From;
//            StateId = fileDirectoryMessage.MessageId;
//            HandleDirectoryMessage(fileDirectoryMessage);
//        }

//        private void HandleDirectoryMessage(MessageEnvelope fileDirectoryMessage)
//        {
//            sw = Stopwatch.StartNew();
//            fileTree = services.FileShare.HandleDirectoryStructure(fileDirectoryMessage);
//            TotalSize = fileTree.TotalSize;
//            int howManyFiles = fileTree.FileStructure.Values.Select(x => x.Count).Sum();
//            GetNext(0);
//        }

//        public void HandleMessage(MessageEnvelope message)
//        {
//            if(message.Header == MessageHeaders.FileTransfer)
//            {
//                HandleFileChunk(message);
//            }
//            else if(message.Header == "FTComplete")
//            {
//                var msg = new MessageEnvelope()
//                {
//                    MessageId = StateId,
//                    Header = "AllGood",
//                };

//                services.MessageHandler.SendAsyncMessage(AssociatedPeer, msg, forceTCP);
//                OnCompleted?.Invoke(new Completion() { Directory = message.KeyValuePairs.Keys.First(), elapsed=sw.Elapsed });
//            }
//            else if (message.Header == "Cancel")
//            {
//                Cancel("Remote " + message.KeyValuePairs["Why"]);
//            }
//        }
//        public void Cancel(string why )
//        {
//            OnProgress = null;
//            Cancelled?.Invoke(new Completion() {AdditionalInfo = why, Directory = fileTree.seed});
//        }
//        private void HandleFileChunk(MessageEnvelope message)
//        {
//            try
//            {
//                var fileMsg = services.FileShare.HandleFileTransferMessage(message, out string error);
//                if (!string.IsNullOrEmpty(error))
//                {
//                    CancelExplicit("File Integrity Corrupted");
//                    return;
//                }
//                Interlocked.Add(ref TotalReceived, message.PayloadCount);
//                GetNext(message.PayloadCount);
//                UpdateStatus(fileMsg);
//            }
//            catch(Exception e)
//            {
//                CancelExplicit("Exception Occurred : " + e.ToString());
//            }
//        }

//        public void CancelExplicit(string why)
//        {
//            Cancel(why);
//            var msg = new MessageEnvelope()
//            {
//                MessageId = StateId,
//                Header = "Cancel",
//                KeyValuePairs = new Dictionary<string, string>() { { "Why", why } }

//            };

//            services.MessageHandler.SendAsyncMessage(AssociatedPeer, msg, forceTCP);
//        }

//        private void UpdateStatus(FileChunk fileMsg)
//        {
//            float percentage = (100 * ((float)fileMsg.SequenceNumber / (float)(fileMsg.TotalSequences)));
//            OnProgress?.Invoke(new TransferStatus() {Percentage= percentage,FileName=fileMsg.FilePath });
//        }

//        private void GetNext(int prevPayloadCount)
//        {
//            var response = new MessageEnvelope()
//            {
//                MessageId = StateId,
//                Header = "Next",
//                Payload = BitConverter.GetBytes(prevPayloadCount)
//            };

//            services.MessageHandler.SendAsyncMessage(AssociatedPeer, response, forceTCP);
//        }
//    }
//}

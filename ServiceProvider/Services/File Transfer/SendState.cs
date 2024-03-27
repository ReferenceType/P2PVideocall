using NetworkLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Videocall.Services.File_Transfer
{
    class SendState : IFileTransferState
    {
        public int Progress { get=>(int)(100*((double)TotalSent/(double)TotalSize)); }
        public Guid StateId { get; private set; }
        private Stopwatch sw;
        public Guid AssociatedPeer { get; private set; }
        public Action<TransferStatus> OnProgress;
        public Action<Completion> Completed;
        public Action<Completion> Cancelled;
        private FileDirectoryStructure tree;
        private List<FileChunk> fileDatas;

        public long TotalSize { get; private set; } = 1;
        private long TotalSent =1;
        private string name;

        private int currentChunk = -1;
        int unAcked = 0;
        int totalAcked = 0;
        int totalOnWire = 0;
        int WindowSize = FileTransferStateManager.WindowsSize;
        //int windowSize = Math.Max(1, (int)(WindowSize / int.Parse(PersistentSettingConfig.Instance.ChunkSize)));
        private int AllSent;

        private bool forceTCP => services.MessageHandler.FTTransportLayer == "Tcp";
        private ServiceHub services => ServiceHub.Instance;
        private AutoResetEvent signal = new AutoResetEvent(false);
        int exit = 0;
        public SendState(string[] files, Guid selectedPeer)
        {
            AssociatedPeer = selectedPeer;
            StateId = Guid.NewGuid();
            Thread thread = new Thread(SendRoutine);
            thread.Start();
            sw = Stopwatch.StartNew();
            ThreadPool.UnsafeQueueUserWorkItem((s) =>
                ExtractDirectoryTree(files), null);
          
        }
      
        private void ExtractDirectoryTree(string[] files)
        {
            try
            {
                tree = services.FileShare.CreateDirectoryTree(files[0]);
                fileDatas = services.FileShare.GetFiles(ref tree, chunkSize: FileTransferStateManager.ChunkSize);
                TotalSize = tree.TotalSize;

                var message = new MessageEnvelope()
                {
                    Header = "FileDirectoryStructure",
                    MessageId = StateId
                };


                services.MessageHandler.SendAsyncMessage(AssociatedPeer, message, tree, forceTCP);
            }
            catch(Exception e)
            {
                Cancel("Transfer Cancelled due to Exception:"+e.ToString());
            }
            
        }
        public void HandleMessage(MessageEnvelope envelope)
        {
            if (envelope.Header == "Next")
            {
                int count = BitConverter.ToInt32(envelope.Payload, envelope.PayloadOffset);
                Interlocked.Add(ref totalOnWire, -count);

                Interlocked.Decrement(ref unAcked);
                int totAck = Interlocked.Increment(ref totalAcked);
                if (totAck == fileDatas.Count)
                {
                    CheckFinalisation();
                }
                int idx = totAck - 1;
                if(idx >0 && idx < fileDatas.Count)
                    Interlocked.Add(ref TotalSent, fileDatas[totAck - 1].count);

                SendNextChunk();
            }
            else if (envelope.Header == "Cancel")
            {
                Cancel("Remote "+envelope.KeyValuePairs["Why"]);
            }
            else if (envelope.Header == "AllGood")
            {
                Completed?.Invoke(new Completion { Directory = tree.seed + name, elapsed = sw.Elapsed });
            }
        }
        public void Cancel(string why)
        {
            Cancelled?.Invoke(new Completion() {AdditionalInfo=why, Directory = tree.seed });
        }

        private void SendRoutine()
        {
            var buffer = new byte[FileTransferStateManager.ChunkSize];

            while (true)
            {
                signal.WaitOne();
                if (Interlocked.CompareExchange(ref exit, 0, 0) == 1)
                    return;

                while (true)
                {
                    if (Interlocked.CompareExchange(ref exit, 0, 0) == 1)
                        return;

                    var cc = Interlocked.Increment(ref currentChunk);
                    if (cc == fileDatas.Count)
                    {
                        // done
                        Interlocked.Exchange(ref AllSent, 1);
                        CheckFinalisation();
                        break;
                    }
                    else if (cc > fileDatas.Count)
                        break;

                    var fileData = fileDatas[cc];
                    fileData.ReadBytesInto(buffer,0,out int cnt);
                    CalcHash(fileData);


                    var envelope = fileData.ConvertToMessageEnvelope(out byte[] chunkBuffer);
                    envelope.MessageId = StateId;
                    services.MessageHandler.SendAsyncMessage(AssociatedPeer, envelope, forceTCP);

                    PublishStatus(fileData);
                    fileData.Release();

                    if (Interlocked.Add(ref totalOnWire, fileData.count) > WindowSize)
                        break;


                }

            }
        }
        private readonly object mtex = new object();
        private void SendNextChunk()
        {
            signal.Set();
            return;
            try
            {
                if (Interlocked.Increment(ref currentChunk) == fileDatas.Count)
                {
                    // done
                    Interlocked.Exchange(ref AllSent, 1);
                    CheckFinalisation();
                }
                else if (Interlocked.CompareExchange(ref currentChunk, 0, 0) < fileDatas.Count)
                {
                    var fileData = fileDatas[currentChunk];
                    fileData.ReadBytes();
                    CalcHash(fileData);

                    int numUnacked = Interlocked.Increment(ref unAcked);

                    var envelope = fileData.ConvertToMessageEnvelope(out byte[] chunkBuffer);
                    envelope.MessageId = StateId;
                    services.MessageHandler.SendAsyncMessage(AssociatedPeer, envelope, forceTCP);

                    PublishStatus(fileData);
                    fileData.Release();

                    if (Interlocked.Add(ref totalOnWire, fileData.count) < WindowSize)
                    {
                        SendNextChunk();
                    }
                }
            }
            catch (Exception e)
            {
                CancelExplicit("Exception Occurred : " + e.ToString());
            }

            //try
            //{

            //    while (true)
            //    {
            //        lock (mtex)
            //        {
            //            var cc = Interlocked.Increment(ref currentChunk);
            //            if (cc == fileDatas.Count)
            //            {
            //                // done
            //                Interlocked.Exchange(ref AllSent, 1);
            //                CheckFinalisation();
            //                break;
            //            }
            //            else if (cc > fileDatas.Count)
            //                break;

            //            var fileData = fileDatas[cc];
            //            fileData.ReadBytes();
            //            CalcHash(fileData);

            //            int numUnacked = Interlocked.Increment(ref unAcked);

            //            var envelope = fileData.ConvertToMessageEnvelope(out byte[] chunkBuffer);
            //            envelope.MessageId = StateId;
            //            services.MessageHandler.SendAsyncMessage(AssociatedPeer, envelope, forceTCP);

            //            PublishStatus(fileData);
            //            fileData.Release();

            //            if (Interlocked.Add(ref totalOnWire, fileData.count) > WindowSize)
            //                break;
            //        }

            //    }

            //}
            //catch (Exception e)
            //{
            //    CancelExplicit("Exception Occurred : " + e.ToString());
            //}

        }
        //private void SendNextChunk2()
        //{
        //    try
        //    {
        //        if (Interlocked.Increment(ref currentChunk) == fileDatas.Count)
        //        {
        //            // done
        //            Interlocked.Exchange(ref AllSent, 1);
        //        }
        //        else if (Interlocked.CompareExchange(ref currentChunk, 0, 0) < fileDatas.Count)
        //        {
        //            var fileData = fileDatas[currentChunk];
        //            TotalSent += fileData.count;

        //            int numUnacked = Interlocked.Increment(ref unAcked);

        //            if (fileData.IsLast)
        //            {
        //                fileData.ReadBytes();
        //                CalcHash(fileData);
        //            }

        //            var envelope = fileData.ConvertToMessageEnvelope(out byte[] chunkBuffer);
        //            envelope.MessageId = StateId;
        //            SendData(envelope, fileData);

        //            PublishStatus(fileData);
        //            fileData.Release();

        //            if (numUnacked < WindowSize)
        //            {
        //                SendNextChunk();
        //            }
        //        }
        //    }
        //    catch(Exception e)
        //    {
        //        CancelExplicit("Exception Occured : "+e.ToString());
        //    }
           
        //}
        //private void SendData(MessageEnvelope envelope,FileChunk fileData)
        //{
        //    services.MessageHandler.SendStreamMessage(AssociatedPeer, envelope, true, (stream) =>
        //    {
        //        if (!fileData.IsLast)
        //        {
        //            fileData.ReadBytesInto(stream);
        //            CalcHash(fileData);
        //        }
        //        else
        //        {
        //            stream.Write(fileData.Data, fileData.dataBufferOffset, fileData.count);
        //        }

        //    }, forceTCP);
        //}
        System.IO.Hashing.XxHash64 hasher = new System.IO.Hashing.XxHash64();

        private void CalcHash(FileChunk fileData)
        {

            hasher.Append(new ReadOnlySpan<byte>(fileData.Data, fileData.dataBufferOffset, fileData.count));
            if (fileData.IsLast)
            {
                fileData.Hashcode = BitConverter.ToString(hasher.GetHashAndReset()).Replace("-", "").ToLower();
            }
        }

        public void CancelExplicit(string why)
        {
            Cancel(why);
            var msg = new MessageEnvelope()
            {
                MessageId = StateId,
                Header = "Cancel",
                KeyValuePairs = new Dictionary<string, string>() { { "Why",why} }
            };

            services.MessageHandler.SendAsyncMessage(AssociatedPeer, msg, forceTCP);
            Cleanup();
        }

        private void PublishStatus(FileChunk fileData)
        {
            float progress = (100 * ((float)fileData.SequenceNumber / (float)(fileData.TotalSequences)));
            TransferStatus status = new TransferStatus()
            {
                Percentage = progress,
                FileName = fileData.FilePath
            };
            OnProgress(status);
        }
        private void CheckFinalisation()
        {
            if(Interlocked.CompareExchange(ref AllSent, 2, 1) == 1)
            {
                 HandleCompletion();
            }
        }
        private void HandleCompletion()
        {
            name = "";
            var firstFolder = tree.FileStructure.Keys.First();

            if (!string.IsNullOrEmpty(firstFolder))
                name += firstFolder;
            else
                name += tree.FileStructure.Values.First().First().FileName;

            //mainWindowViewModel.WriteInfoEntry("Transfer Complete in " + sw.Elapsed.ToString(), tree.seed + name);

            var response = new MessageEnvelope();
            response.Header = "FTComplete";
            response.MessageId = StateId;
            response.KeyValuePairs = new Dictionary<string, string>() { { name, null } };
            services.MessageHandler.SendAsyncMessage(AssociatedPeer, response, forceTCP);

            Interlocked.Exchange(ref exit, 1);
            signal.Set();
        }

        internal void Cleanup()
        {
            Interlocked.Exchange(ref exit, 1);
            signal.Set();
        }
    }
}

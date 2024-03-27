using NetworkLibrary.P2P.Generic;
using NetworkLibrary;
using ProtoBuf.Meta;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Security.Policy;
using System.Collections.Concurrent;
using System.Xml.Linq;
using System.Xml;

namespace Videocall.Services.File_Transfer
{
    public interface IFileTransferState
    {
        Guid AssociatedPeer {  get; }
        Guid StateId {  get; }
        void Cancel(string why);
        void CancelExplicit(string why);
        void HandleMessage(MessageEnvelope envelope);

        int Progress { get; }
        long TotalSize { get; }
    }
    public class TransferStatus
    {
        public string FileName;
        public float Percentage;

    }
    public class Completion
    {
        public string Directory;
        public string AdditionalInfo;
        public TimeSpan elapsed;
    }

    public class FileTransferStateManager
    {
        public Action<IFileTransferState, TransferStatus> OnTransferStatus;
        public Action<IFileTransferState, TransferStatus> OnReceiveStatus;
        public Action<IFileTransferState,Completion> OnTransferComplete;
        public Action<IFileTransferState,Completion> OnReceiveComplete;
        public Action<IFileTransferState,Completion> OnTransferCancelled;
        public Action<IFileTransferState,Completion> OnReceiveCancelled;
        private ServiceHub services => ServiceHub.Instance;
        private ConcurrentDictionary<Guid, IFileTransferState> activeStates = new ConcurrentDictionary<Guid, IFileTransferState>();

        public static int WindowsSize = 12800000;
        public static int ChunkSize = 128000;
        public void CancelExplicit(Guid stateId)
        {
            if(activeStates.TryGetValue(stateId, out var state))
            {
                state.CancelExplicit("User Cancelled");
            }
        }

        public void CancelAssociatedState(Guid peerId)
        {
            foreach (var state in activeStates.Values)
            {
                if(state.AssociatedPeer == peerId)
                {
                    state.Cancel("Cancelled due to Disconnect");
                    activeStates.TryRemove(state.StateId, out _);
                }
            }
        }

        public void SendFile(string[] files, Guid selectedPeer)
        {
            var sendState = new SendState(files, selectedPeer);
            sendState.OnProgress += (sts) => OnTransferStatus?.Invoke(sendState,sts);
            sendState.Completed =(c) => HandleCompletedSend(sendState,c);
            sendState.Cancelled =(c) => HandleCancelledSend(sendState,c);
            activeStates.TryAdd(sendState.StateId, sendState);
        }

        public void HandleReceiveFile(MessageEnvelope message)
        {
           var receiveState =  new ReceiveState(message);
            receiveState.OnProgress += (sts) => OnReceiveStatus?.Invoke(receiveState,sts);
            receiveState.OnCompleted = (c) => HandleCompletionReceive(receiveState, c);
            receiveState.Cancelled = (c) => HandleCancelledReceive(receiveState, c);
            activeStates.TryAdd(receiveState.StateId, receiveState);
        }

        private void HandleCancelledSend(SendState sendState, Completion c)
        {
            OnTransferCancelled?.Invoke(sendState, c);
            activeStates.TryRemove(sendState.StateId, out _);
            services.FileShare.CleanUp(sendState.StateId);
            sendState.Cleanup();
        }

        private void HandleCompletedSend(SendState sendState, Completion completion)
        {
            OnTransferComplete?.Invoke(sendState, completion);
            activeStates.TryRemove(sendState.StateId, out _);
            services.FileShare.CleanUp(sendState.StateId);
            sendState.Cleanup();

        }

        private void HandleCancelledReceive(ReceiveState receiveState, Completion c)
        {
            OnReceiveCancelled?.Invoke(receiveState,c);
            activeStates.TryRemove(receiveState.StateId, out _);
            services.FileShare.CleanUp(receiveState.StateId);
        }

        private void HandleCompletionReceive(ReceiveState receiveState,Completion completion)
        {
            OnReceiveComplete?.Invoke(receiveState,completion);
            activeStates.TryRemove(receiveState.StateId, out _);
            services.FileShare.CleanUp(receiveState.StateId);
        }

        public bool HandleMessage(MessageEnvelope message)
        {
            if (activeStates.TryGetValue(message.MessageId, out var state))
            {
                state.HandleMessage(message);
                return true;
            }
            return false;
        }

        public void CancelAllStates()
        {
            foreach (var state in activeStates.Values)
            {
                state.CancelExplicit("User Cancelled");
            }

            services.FileShare.ReleaseAll();
        }
    }
}

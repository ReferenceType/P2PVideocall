using NetworkLibrary.Utils;
using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;

namespace Videocall.Models.MainWindow
{
    [ProtoContract]
    class ChatSerializationData:IProtoMessage
    {
        [ProtoContract]
        public enum MsgType
        {
            Local,
            Remote,
            Info
        }

        [ProtoMember(1)]
        public MsgType MessageType { get; set; }
        [ProtoMember(2)]
        public string Sender { get; set; }
        [ProtoMember(3)]
        public string Message { get; set; }

        [ProtoMember(4)]
        public DateTime TimeStamp { get; set; }

    }
    internal class ChatSerializer
    {
        ConcurrentProtoSerialiser serializer = new ConcurrentProtoSerialiser();
        private int lastOffset=0;
        private bool allMessagesLoaded;

        public string PathIndex { get; private set; }
        public string PathData { get; private set; }

        private readonly object StreamLocker = new object();  
        public ChatSerializer(string path)
        {
            PathData = path+@"\data.bin";
            new FileStream(PathData, FileMode.OpenOrCreate).Dispose();
            
        }

        public void SerializeRemoteEntry(string sender, string message, DateTime timestamp)
        {
            ChatSerializationData data = new ChatSerializationData 
            {
                MessageType = ChatSerializationData.MsgType.Remote,
                Sender = sender, 
                Message = message,
                TimeStamp = timestamp
            };
            SerializeIntoStream(data);
        }
        public void SerializeLocalEntry(string message, DateTime timestamp, string sender) 
        {
            ChatSerializationData data = new ChatSerializationData
            {
                MessageType = ChatSerializationData.MsgType.Local,
                Sender= sender,
                Message = message, 
                TimeStamp = timestamp
                
            };
            SerializeIntoStream(data);
        }
        public void SerializeInfoEntry(string message, DateTime timestamp)
        {
            ChatSerializationData data = new ChatSerializationData
            {
                MessageType = ChatSerializationData.MsgType.Info,
                Message = message, 
                TimeStamp = timestamp
            };

            SerializeIntoStream(data);
           
        }

        private void SerializeIntoStream(ChatSerializationData data)
        {
            lock (StreamLocker)
            {
                using (var streamData = new FileStream(PathData, FileMode.Append))
                {
                    // prefix + postfix in case ending is corrupted
                    var bytes = serializer.Serialize(data);
                    int lenght = bytes.Length;
                    var msgByteLength = BitConverter.GetBytes(lenght);

                    streamData.Write(msgByteLength, 0, 4);
                    streamData.Write(bytes, 0, bytes.Length);
                    streamData.Write(msgByteLength, 0, 4);

                    streamData.Flush();
                    lastOffset += bytes.Length + 8;
                };
            }

        }

        
        public bool LoadFromEnd(int maxAmount, out List<ChatSerializationData> messages)
        {
            lock (StreamLocker)
            {
                messages = null;
                if (allMessagesLoaded)
                {
                    return false;
                }

                using (var streamData = new FileStream(PathData, FileMode.Open))
                {
                    // seek start + 4,                      -> get len
                    // seek start + len + 4,                -> get msg
                    // seek start + len + 12,               -> get len2
                    // seek start + len + len2 + 12,        ...
                    // seek start + len + len2 + 20
                    // seek start + len + len2 + len3 + 20

                    bool retval = false;
                    try
                    {
                        int offset = lastOffset;
                        byte[] suffix = new byte[4];
                        messages = new List<ChatSerializationData>();
                        int numMessages = 0;
                        while (numMessages < maxAmount && (streamData.Length >= offset + 8))
                        {
                            var pos = streamData.Seek(-(offset + 4), SeekOrigin.End);
                            streamData.Read(suffix, 0, 4);
                            var lenght = BitConverter.ToInt32(suffix, 0);

                            streamData.Seek(-(lenght + offset + 4), SeekOrigin.End);
                            byte[] message = new byte[lenght];
                            streamData.Read(message, 0, message.Length);

                            var msg = serializer.Deserialize<ChatSerializationData>(message,0,message.Length);
                            messages.Add(msg);

                            numMessages++;
                            offset += lenght + 8;
                            lastOffset = offset;
                            retval = true;
                        }
                        if((streamData.Length < offset + 8)) allMessagesLoaded=true;
                        return retval;
                    }
                    catch(Exception ex)
                    {

                        return false;
                    }
                  

                }

            }

        }

        public void ClearAllHistory()
        {
            lock (StreamLocker)
            {
                allMessagesLoaded = true;
                System.IO.File.WriteAllText(PathData, string.Empty);
            }

                
        }
    }
}

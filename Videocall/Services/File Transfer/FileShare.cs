using NetworkLibrary;
using NetworkLibrary.Components;
using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Videocall
{
    [DebuggerDisplay("FileName")]
    [ProtoContract]
    public class FileData
    {
        [ProtoMember(1)]
        public long Size;
        [ProtoMember(2)]
        public string FileName;

        public FileData()
        {
        }

        public FileData(long size, string fileName)
        {
            Size = size;
            FileName = fileName;
        }
    }
    [ProtoContract]
    public class FileDirectoryStructure : IProtoMessage
    {
        internal string seed;
        [ProtoMember(1)]
        public Dictionary<string, List<FileData>> FileStructure;
        [ProtoMember(2)]
        public long TotalSize;
    }

    public class FileChunk
    {
        public string FilePath;
        public byte[] Data;
        public int SequenceNumber;
        public int TotalSequences;

        public bool IsLast => SequenceNumber == TotalSequences - 1;

        internal long FileStreamStartIdx;
        internal int dataBufferOffset;
        internal int count;
        internal string seed;
        internal string Hashcode = "";
        private bool canReturnBuffer = false;
        public FileChunk()
        {
        }

        public FileChunk(string filePath, string seed, long startIndex, int count, int sequenceNumber, int totalSequences)
        {
            this.FilePath = filePath;
            this.SequenceNumber = sequenceNumber;
            this.seed = seed;
            this.FileStreamStartIdx = startIndex;
            this.count = count;
            this.TotalSequences = totalSequences;

        }

        internal void ReadBytes()
        {
            if (count == 0)
                return;

            using (var streamData = new FileStream(seed + FilePath, FileMode.Open, FileAccess.Read, System.IO.FileShare.Read))
            {
                streamData.Seek(FileStreamStartIdx, SeekOrigin.Begin);

                Data = BufferPool.RentBuffer(count);
                canReturnBuffer = true;

                streamData.Read(Data, 0, count);
            }
            if (Data.Length == 0)
                Data = null;

        }

        internal void ReadBytesInto(PooledMemoryStream stream)
        {
            if (count == 0)
                return;
            stream.Reserve(count);
            using (var streamData = new FileStream(seed + FilePath, FileMode.Open, FileAccess.Read, System.IO.FileShare.Read))
            {
                streamData.Seek(FileStreamStartIdx, SeekOrigin.Begin);

                Data = stream.GetBuffer();
                dataBufferOffset = stream.Position32;

                streamData.Read(Data, dataBufferOffset, count);
                stream.Position32 += count;
            }
            
        }
        internal MessageEnvelope ConvertToMessageEnvelope(out byte[] chunkBuffer)
        {
            chunkBuffer = Data;
            var env = new MessageEnvelope();
            var kvPairs = new Dictionary<string, string>();

            kvPairs["0"] = FilePath;
            kvPairs["1"] = SequenceNumber.ToString();
            kvPairs["2"] = TotalSequences.ToString();
            kvPairs["3"] = FileStreamStartIdx.ToString();
            kvPairs["4"] = Hashcode;

            env.KeyValuePairs = kvPairs;
            env.SetPayload(Data, dataBufferOffset, count);
            env.Header = MessageHeaders.FileTransfer;
            return env;
        }

        internal static FileChunk CreateFromMessage(MessageEnvelope msg)
        {
            var ft = new FileChunk();
            ft.FilePath = msg.KeyValuePairs["0"];
            ft.SequenceNumber = int.Parse(msg.KeyValuePairs["1"]);
            ft.TotalSequences = int.Parse(msg.KeyValuePairs["2"]);
            ft.FileStreamStartIdx = long.Parse(msg.KeyValuePairs["3"]);
            ft.Hashcode = msg.KeyValuePairs["4"];

            ft.Data = msg.Payload;
            ft.dataBufferOffset = msg.PayloadOffset;
            ft.count = msg.PayloadCount;
            return ft;

        }

        internal void Release()
        {
            if (Data != null && canReturnBuffer)
                BufferPool.ReturnBuffer(Data);
        }

        internal void ComputeHash()
        {
            System.IO.Hashing.XxHash64 hasher = new System.IO.Hashing.XxHash64();
            //using (var md5 = new MD5CryptoServiceProvider())
            {
                using (var stream = File.OpenRead(seed + FilePath))
                {
                    hasher.Append(stream);
                    Hashcode = BitConverter.ToString(hasher.GetCurrentHash()).Replace("-", "").ToLower();
                    //Hashcode = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
                }
            }
        }
    }
    class FileState
    {
        
        public FileStream FStream;
        public System.IO.Hashing.XxHash64 hasher = new System.IO.Hashing.XxHash64();

        public Guid StateId;
        public FileState(FileStream fileStream, Guid stateId)
        {
            FStream = fileStream;
            StateId = stateId;
        }
    }
    internal class FileShare
    {
        static ConcurrentProtoSerialiser serialiser = new ConcurrentProtoSerialiser();
        private readonly ConcurrentDictionary<string, FileState> OpenFiles = new ConcurrentDictionary<string, FileState>();
        private readonly string SharedFolderDir;
        private readonly object fileLocker = new object();

        public FileShare()
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            SharedFolderDir = path + "/Shared";
        }
        public FileDirectoryStructure CreateDirectoryTree(string path)
        {
            FileDirectoryStructure structure = new FileDirectoryStructure();
            structure.FileStructure = new Dictionary<string, List<FileData>>();
            string TopFolderName = Directory.GetParent(path).FullName;

            structure.seed = TopFolderName;
            if (Directory.Exists(path))
            {
                var pathCut = path.Replace(TopFolderName, "");
                structure.FileStructure[pathCut] = new List<FileData>();

                var filesTop = Directory.GetFiles(path);
                foreach (var file in filesTop)
                {
                    var fileCut = file.Replace(TopFolderName, "");
                    structure.FileStructure[pathCut].Add(new FileData(0,fileCut));
                }

                string[] subdirs = Directory.GetDirectories(path, "*", searchOption: SearchOption.AllDirectories);
                foreach (var subdir in subdirs)
                {
                    var subdirCut = subdir.Replace(TopFolderName, "");
                    structure.FileStructure[subdirCut] = new List<FileData>();

                    var files = Directory.GetFiles(subdir);
                    foreach (var file in files)
                    {
                        var fileCut = file.Replace(TopFolderName, "");
                        structure.FileStructure[subdirCut].Add(new FileData(0,fileCut));
                    }
                }
            }
            else if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path);

                var dirCut = dir.Replace(TopFolderName, "");
                var pathCut = path.Replace(TopFolderName, "");
                structure.FileStructure[dirCut] = new List<FileData>() { new FileData(0,pathCut) };

            }
            return structure;

        }

        public List<FileChunk> GetFiles(ref FileDirectoryStructure ds, int chunkSize = 1280000)
        {
            // Add info to ds here. Single Pass;
            List<FileChunk> chunks = new List<FileChunk>();

            foreach (var folder in ds.FileStructure)
            {
                foreach (var fileInfo in folder.Value)
                {

                    var filePath = fileInfo.FileName;
                    FileInfo fi = new FileInfo(ds.seed + filePath);
                    fileInfo.Size = fi.Length;
                    ds.TotalSize += fi.Length;
                    if (fi.Length > chunkSize)
                    {
                        int remain = (int)(fi.Length % chunkSize);
                        int num = (int)(fi.Length / chunkSize);
                        int totalSeq = num;
                        if (remain > 0) totalSeq++;

                        int i = 0;
                        for (i = 0; i < num; i++)
                        {
                            chunks.Add(new FileChunk(filePath, ds.seed, (long)chunkSize * i, chunkSize, i, totalSeq));

                        }
                        if (remain > 0)
                            chunks.Add(new FileChunk(filePath, ds.seed, (long)chunkSize * i, remain, i, totalSeq));

                    }
                    else
                        chunks.Add(new FileChunk(filePath, ds.seed, 0, (int)fi.Length, 0, 1));
                    // we hash while we read now
                   // chunks.Last().ComputeHash();

                };
              
            }
            return chunks;
        }
        public FileChunk HandleFileTransferMessage(MessageEnvelope msg, out string error)
        {

            try
            {
                lock (fileLocker)
                {
                    error = null;
                    var ftMsg = FileChunk.CreateFromMessage(msg);
                    string fileDir = Path.GetDirectoryName(SharedFolderDir + ftMsg.FilePath);
                    string filePath = SharedFolderDir + ftMsg.FilePath;

                    if (!OpenFiles.ContainsKey(filePath))
                    {
                        if (!Directory.Exists(fileDir))
                        {
                            Directory.CreateDirectory(fileDir);
                        }

                        if (/*ftMsg.SequenceNumber == 0 && */File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                        var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, System.IO.FileShare.Read);
                        OpenFiles[filePath] = new FileState(fs, msg.MessageId);
                    }

                    if (ftMsg.Data == null || ftMsg.count == 0)
                        return ftMsg;

                    var FileState = OpenFiles[filePath];
                    var streamData = FileState.FStream;
                    streamData.Seek(ftMsg.FileStreamStartIdx, SeekOrigin.Begin);
                    streamData.Write(ftMsg.Data, ftMsg.dataBufferOffset, ftMsg.count);
                    // Corruption Test
                    //ftMsg.Data[ftMsg.dataBufferOffset] = 55;

                    var hasher = FileState.hasher;
                    if (ftMsg.IsLast)
                    {
                        if (OpenFiles.TryRemove(filePath, out var fileState))
                        {
                            fileState.FStream.Dispose();
                            
                            hasher.Append(new ReadOnlySpan<byte>(ftMsg.Data, ftMsg.dataBufferOffset, ftMsg.count));
                            string  Hashcode = BitConverter.ToString(hasher.GetCurrentHash()).Replace("-", "").ToLower();
                            if (Hashcode != ftMsg.Hashcode)
                            {
                                error = filePath + " is Corrupted";
                                Console.WriteLine(error);
                            }
                            Console.WriteLine(Hashcode);
                        }
                    }
                    else
                    {
                        hasher.Append(new Span<byte>(ftMsg.Data, ftMsg.dataBufferOffset, ftMsg.count));
                    }

                    return ftMsg;
                }
            }
            catch(Exception e)
            { 
                error = e.Message;
                return null;
            }
            
        }

        public FileDirectoryStructure HandleDirectoryStructure(MessageEnvelope msg)
        {
            var structureMSg = serialiser.UnpackEnvelopedMessage<FileDirectoryStructure>(msg);
            return structureMSg;
        }

        public void CleanUp(Guid stateID)
        {
            lock (fileLocker)
            {
                foreach (var item in OpenFiles)
                {
                    if (item.Value.StateId == stateID)
                    {
                        OpenFiles.TryRemove(item.Key, out _);
                        try
                        {
                            item.Value.FStream.Close();
                            File.Delete(item.Key);
                        }
                        catch { }
                      
                    }
                }
            }
             
        }
        public void ReleaseAll()
        {
            lock (fileLocker)
            {
                foreach (var openFile in OpenFiles)
                {
                    try
                    {
                        openFile.Value.FStream.Close();
                        File.Delete(openFile.Key);
                    }
                    catch { }

                }
                OpenFiles.Clear();
            }
        }
    }
}
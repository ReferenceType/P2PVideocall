using NetworkLibrary;
using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Videocall
{
    [ProtoContract]
    public class FileDirectoryStructure : IProtoMessage
    {
        internal string seed;
        [ProtoMember(1)]
        public Dictionary<string, List<string>> FileStructure;
    }

    public class FileTransfer
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
        public FileTransfer()
        {
        }

        public FileTransfer(string filePath, string seed, long startIndex, int count, int sequenceNumber, int totalSequences)
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

            using (var streamData = new FileStream(seed + FilePath, FileMode.Open, FileAccess.Read, System.IO.FileShare.ReadWrite))
            {
                streamData.Seek(FileStreamStartIdx, SeekOrigin.Begin);

                Data = BufferPool.RentBuffer(count);
                streamData.Read(Data, 0, count);
            }
            if (Data.Length == 0)
                Data = null;

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

        internal static FileTransfer CreateFromMessage(MessageEnvelope msg)
        {
            var ft = new FileTransfer();
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
            if (Data != null)
                BufferPool.ReturnBuffer(Data);
        }

        internal void ComputeHash()
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(seed + FilePath))
                {
                    Hashcode = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
                }
            }
        }
    }
    class FileState
    {
        public FileStream FStream;
        public MD5 Md5 = new MD5CryptoServiceProvider();

        public FileState(FileStream fileStream)
        {
            FStream = fileStream;
        }
    }
    internal class FileShare
    {
        static ConcurrentProtoSerialiser serialiser = new ConcurrentProtoSerialiser();
        private readonly ConcurrentDictionary<string, FileState> OpenFiles = new ConcurrentDictionary<string, FileState>();
        public FileDirectoryStructure CreateDirectoryTree(string path)
        {
            FileDirectoryStructure structure = new FileDirectoryStructure();
            structure.FileStructure = new Dictionary<string, List<string>>();
            string TopFolderName = Directory.GetParent(path).FullName;

            structure.seed = TopFolderName;
            if (Directory.Exists(path))
            {
                var pathCut = path.Replace(TopFolderName, "");
                structure.FileStructure[pathCut] = new List<string>();

                var filesTop = Directory.GetFiles(path);
                foreach (var file in filesTop)
                {
                    var fileCut = file.Replace(TopFolderName, "");
                    structure.FileStructure[pathCut].Add(fileCut);
                }

                string[] subdirs = Directory.GetDirectories(path, "*", searchOption: SearchOption.AllDirectories);
                foreach (var subdir in subdirs)
                {
                    var subdirCut = subdir.Replace(TopFolderName, "");
                    structure.FileStructure[subdirCut] = new List<string>();

                    var files = Directory.GetFiles(subdir);
                    foreach (var file in files)
                    {
                        var fileCut = file.Replace(TopFolderName, "");
                        structure.FileStructure[subdirCut].Add(fileCut);
                    }
                }
            }
            else if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path);

                var dirCut = dir.Replace(TopFolderName, "");
                var pathCut = path.Replace(TopFolderName, "");

                structure.FileStructure[dirCut] = new List<string>() { pathCut };

            }
            return structure;

        }

        public List<FileTransfer> GetFiles(FileDirectoryStructure ds, int chunkSize = 1280000)
        {
            List<FileTransfer> files = new List<FileTransfer>();

            foreach (var folder in ds.FileStructure)
            {
                foreach (var filePath in folder.Value)
                {
                    FileInfo fi = new FileInfo(ds.seed + filePath);
                    if (fi.Length > chunkSize)
                    {
                        int remain = (int)(fi.Length % chunkSize);
                        int num = (int)(fi.Length / chunkSize);
                        int totalSeq = num;
                        if (remain > 0) totalSeq++;

                        int i = 0;
                        for (i = 0; i < num; i++)
                        {
                            files.Add(new FileTransfer(filePath, ds.seed, (long)chunkSize * i, chunkSize, i, totalSeq));

                        }
                        if (remain > 0)
                            files.Add(new FileTransfer(filePath, ds.seed, (long)chunkSize * i, remain, i, totalSeq));

                    }
                    else
                        files.Add(new FileTransfer(filePath, ds.seed, 0, (int)fi.Length, 0, 1));
                    files.Last().ComputeHash();

                };
              
            }
            return files;
        }

        public FileTransfer HandleFileTransferMessage(MessageEnvelope msg, out string error)
        {
            error= null;
            var ftMsg = FileTransfer.CreateFromMessage(msg);

            string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string sharedFolder = path + "/Shared";
            string fileDir = Path.GetDirectoryName(sharedFolder + ftMsg.FilePath);

            if (!Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }

            string filePath = sharedFolder + ftMsg.FilePath;
            if (ftMsg.SequenceNumber == 0 && File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            if (!File.Exists(filePath))
                OpenFiles[filePath] =
                    new FileState(new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, System.IO.FileShare.Read));

            if (ftMsg.Data == null || ftMsg.count == 0)
                return ftMsg;

            var streamData = OpenFiles[filePath].FStream;
            streamData.Seek(ftMsg.FileStreamStartIdx, SeekOrigin.Begin);
            streamData.Write(ftMsg.Data, ftMsg.dataBufferOffset, ftMsg.count);

            var md5 = OpenFiles[filePath].Md5;
            if (ftMsg.IsLast)
            {
                if (OpenFiles.TryRemove(filePath, out var fileState))
                {
                    fileState.FStream.Dispose();
                    md5.TransformFinalBlock(ftMsg.Data, ftMsg.dataBufferOffset, ftMsg.count);
                    string Hashcode = BitConverter.ToString(md5.Hash).Replace("-", "").ToLower();

                    if (Hashcode != ftMsg.Hashcode)
                        error = filePath + " is Corrupted";
                    md5.Dispose();
                }
            }
            else
            {
                OpenFiles[filePath].Md5.TransformBlock(ftMsg.Data, ftMsg.dataBufferOffset, ftMsg.count, ftMsg.Data, ftMsg.dataBufferOffset);

            }

            return ftMsg;
        }

        public FileDirectoryStructure HandleDirectoryStructure(MessageEnvelope msg)
        {
            var structureMSg = serialiser.UnpackEnvelopedMessage<FileDirectoryStructure>(msg);
            return structureMSg;
        }

    }
}
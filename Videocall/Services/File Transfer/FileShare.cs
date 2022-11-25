using ProtoBuf;
using Protobuff;
using System;
using System.Collections.Generic;
using System.IO;


namespace Videocall
{
    [ProtoContract]
    public class FileDirectoryStructure: IProtoMessage
    {
        internal string seed;
        [ProtoMember(1)]
        public Dictionary<string, List<string>> FileStructure;
    }

    [ProtoContract]
    public class FileTransfer:IProtoMessage
    {
        [ProtoMember(1)]
        public string FilePath;
        [ProtoMember(2)]
        public byte[] Data;

        public FileTransfer()
        {
        }

        public FileTransfer(string filePath, byte[] data)
        {
            this.FilePath = filePath;
            this.Data = data;
        }
    }

    internal class FileShare
    {
        static ConcurrentProtoSerialiser serialiser = new ConcurrentProtoSerialiser();
        public FileDirectoryStructure CreateDirectoryTree(string path)
        {
            FileDirectoryStructure structure =  new FileDirectoryStructure();
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

                string [] subdirs = Directory.GetDirectories(path,"*",searchOption: SearchOption.AllDirectories);
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
            else if(File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path);

                var dirCut = dir.Replace(TopFolderName, "");
                var pathCut = path.Replace(TopFolderName, "");

                structure.FileStructure[dirCut] = new List<string>() { pathCut };

            }
            return structure;

        }

        public List<FileTransfer> GetFiles(FileDirectoryStructure ds)
        {
            List<FileTransfer> files = new List<FileTransfer>();
            foreach (var folder in ds.FileStructure)
            {
                foreach (var filePath in folder.Value)
                {
                    var bytes = File.ReadAllBytes(ds.seed + filePath);
                    files.Add(new FileTransfer(filePath, bytes));
                }
            }
            return files;
        }

        public FileTransfer HandleFileTransferMessage(MessageEnvelope msg)
        {
            var ftMsg = serialiser.UnpackEnvelopedMessage<FileTransfer>(msg);
            string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string sharedFolder = path + "/Shared";
            string fileDir = Path.GetDirectoryName(sharedFolder + ftMsg.FilePath);
            if (!Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }
            string filePath = sharedFolder + ftMsg.FilePath;
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            //Path.get
            File.WriteAllBytes(filePath, ftMsg.Data);
            return ftMsg;
        }

        public FileDirectoryStructure HandleDirectoryStructure(MessageEnvelope msg)
        {
            var structureMSg = serialiser.UnpackEnvelopedMessage<FileDirectoryStructure>(msg);
            return structureMSg;
        }

    }
}

//using OpenCvSharp;
//using OpenCvSharp.Extensions;
//using ProtoBuf;
//using ProtoBuf.WellKnownTypes;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Drawing;
//using System.IO.Ports;
//using System.Linq;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;
//using System.Windows;

//namespace Videocall.Services.ScreenShare
//{
//    [ProtoContract]
//    class ScreenImage
//    {
//        [ProtoMember(1)]
//        public byte TotalFragments;

//        [ProtoMember(2)]
//        public byte FragmentNumber;

//        [ProtoMember(3)]
//        public DateTime Timestamp;

//        [ProtoMember(4)]
//        public byte[] Data;

//        private ConcurrentDictionary<byte, byte[]> Images;
//        private int totalBytes;
//        public void Merge(ScreenImage fragment)
//        {
//            if(Images == null)
//            {
//                Images= new ConcurrentDictionary<byte, byte[]>();
//                Images[FragmentNumber] = Data;
//                totalBytes += Data.Length;
//            }
//            Images[fragment.FragmentNumber] = fragment.Data;
//            totalBytes+= fragment.Data.Length;
//        }

//        public bool IsComplete =>Images == null || Images.Count == TotalFragments;

//        public byte[] GetBytes()
//        {
//            if (TotalFragments == 1)
//                return Data;
//            else
//            {
//                byte[] bytes = new byte[totalBytes];
//                int offset = 0;
//                var ordered = Images.OrderByDescending(x=>x.Key);
//                foreach (var image in ordered)
//                {
//                    Buffer.BlockCopy(image.Value, 0, bytes, offset, bytes.Length);
//                    offset += bytes.Length;
//                }
//                return bytes;
//            }

//        }

//    }
//    internal class ScreenShareHandler
//    {
//        private AutoResetEvent imgReady = new AutoResetEvent(false);
//        private ConcurrentDictionary<Timestamp,ScreenImage> images =  new ConcurrentDictionary<Timestamp, ScreenImage>();
//        public ScreenShareHandler() 
//        {
          
//        }

//        public void StartCapture()
//        {
//            double screenLeft = SystemParameters.VirtualScreenLeft;
//            double screenTop = SystemParameters.VirtualScreenTop;
//            double screenWidth = 3840;//SystemParameters.FullPrimaryScreenWidth;
//            double screenHeight = 2400; // SystemParameters.FullPrimaryScreenHeight;
//            Bitmap bmp = new Bitmap((int)screenWidth, (int)screenHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);


//            ImageEncodingParam[] par = new ImageEncodingParam[1];
//            par[0] = new ImageEncodingParam(ImwriteFlags.WebPQuality, 80);

//            while (true)
//            {
//                Graphics g = Graphics.FromImage(bmp);

//                g.CopyFromScreen((int)screenLeft, (int)screenTop, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
//                var resizedBmp = new Bitmap(bmp, (int)1280, (int)720);
//                var mat = resizedBmp.ToMat();

//                var compressed = mat.ImEncode(ext: ".webp", par);
//                var decoded = Cv2.ImDecode(compressed, ImreadModes.Unchanged);
//               // MainVideoCanvas.Source = decoded.ToBitmapSource();
                
//            }
//        }

//        public void StopCapture()
//        {

//        }

//        public void HandleImage(ScreenImage image)
//        {
//            if (image.TotalFragments != 1)
//            {
//                if (images.TryGetValue(image.Timestamp,out var img))
//                {
//                    img.Merge(image);
//                }
//                else
//                {
//                    images.TryAdd(image.Timestamp, image);
//                }

//            }
//            else
//                images.TryAdd(image.Timestamp, image);


//            UpdateState();

//        }

//        private void UpdateState()
//        {
//            var ordered = images.OrderByDescending(x => x.Key);
//            if (ordered.Count() > 3)
//            {
//                images.TryRemove(ordered.First().Key, out _);
//            }
//            if (ordered.Last().Value.IsComplete)
//            {
//                //imgReady send it
//                ImageReady(ordered.Last().Value);
//            }

//        }

//        private void ImageReady(ScreenImage image)
//        {
//           var imgBytes = image.GetBytes();
//        }
//    }
//}

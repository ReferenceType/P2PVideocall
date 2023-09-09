//using NetworkLibrary;
//using OpenCvSharp;
//using OpenCvSharp.Extensions;
//using System;
//using System.Collections.Generic;
//using System.Drawing;
//using System.Linq;
//using System.Runtime.InteropServices;
//using System.Text;
//using System.Threading.Tasks;
//using System.Windows;
//using Windows.Storage.Compression;

//namespace Videocall.Services.ScreenShare
//{
//    internal class SimpleScreenShareHandler
//    {
//        [StructLayout(LayoutKind.Sequential)]
//        struct CursorInfo
//        {
//            public Int32 cbSize;
//            public Int32 flags;
//            public IntPtr hCursor;
//            public PointApi ptScreenPos;
//        }

//        [StructLayout(LayoutKind.Sequential)]
//        struct PointApi
//        {
//            public int x;
//            public int y;
//        }

//        [DllImport("user32.dll")]
//        static extern bool GetCursorInfo(out CursorInfo pci);

//        [DllImport("user32.dll")]
//        static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

//        const Int32 cursorVisible = 0x00000001;


//        public Action<byte[],Mat> LocalImageAvailable;
//        public Action<Mat> RemoteImageAvailable;
//        private bool captureActive = false;
//        public void StartCapture()
//        {
//            if (captureActive) return;
//            captureActive = true;
//            var res = ScreenInformations.GetDisplayResolution();

//            double screenLeft = SystemParameters.VirtualScreenLeft;
//            double screenTop = SystemParameters.VirtualScreenTop;
//            double screenWidth = res.Width;//3840; //SystemParameters.FullPrimaryScreenWidth;
//            double screenHeight = res.Height;//2400; // SystemParameters.FullPrimaryScreenHeight;
//            Bitmap bmp = new Bitmap((int)screenWidth, (int)screenHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);


//            ImageEncodingParam[] par = new ImageEncodingParam[1];
//            par[0] = new ImageEncodingParam(ImwriteFlags.WebPQuality, 80);
//            //par[1] = new ImageEncodingParam(ImwriteFlags.JpegOptimize, 1);
//            //par[2] = new ImageEncodingParam(ImwriteFlags.JpegProgressive, 1);
//            Task secondary = null;
//            Task.Run(async () =>
//            {
//                while (captureActive)
//                {
//                   // await Task.Delay(30);
//                    Graphics g = Graphics.FromImage(bmp);
//                    g.CopyFromScreen((int)screenLeft, (int)screenTop, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);

//                    CursorInfo pci;
//                    pci.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(CursorInfo));

//                    if (GetCursorInfo(out pci))
//                    {
//                        if (pci.flags == cursorVisible)
//                        {
//                            DrawIcon(g.GetHdc(), pci.ptScreenPos.x, pci.ptScreenPos.y, pci.hCursor);
//                            g.ReleaseHdc();
//                        }
//                    }

//                    Mat mat = null;
//                    if (screenWidth > 1920)
//                    {
//                        var resizedBmp = new Bitmap(bmp, (int)1440, (int)810);
//                         mat = resizedBmp.ToMat();
//                    }
//                    else
//                        mat=bmp.ToMat();

//                    //var compressed = mat.ImEncode(ext: ".webp", par);
//                    //Console.WriteLine(compressed.Length);
//                    //LocalImageAvailable?.Invoke(compressed, mat);

//                    if (secondary != null)
//                        await secondary;
//                    secondary = Task.Run(() =>
//                    {
//                        var compressed = mat.ImEncode(ext: ".webp", par);
//                        Console.WriteLine(compressed.Length);
//                        LocalImageAvailable?.Invoke(compressed, mat);
//                    });

//                }

//            });
//        }

//        public void HandleNetworkImageBytes(byte[] payload, int payloadOffset, int payloadCount)
//        {
//            var buff = BufferPool.RentBuffer(payloadCount);
//            Buffer.BlockCopy(payload, payloadOffset, buff, 0, payloadCount);
//            Mat img = new Mat(NativeMethods.imgcodecs_imdecode_vector(buff, new IntPtr(payloadCount), (int)ImreadModes.Color));
//            RemoteImageAvailable?.Invoke(img);
//            BufferPool.ReturnBuffer(buff);
//        }

//        internal void StopCapture()
//        {
//            captureActive= false;
//        }
//    }
//}

using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Windows.Storage.Compression;

namespace Videocall.Services.ScreenShare
{
    internal class SimpleScreenShareHandler
    {
        [StructLayout(LayoutKind.Sequential)]
        struct CURSORINFO
        {
            public Int32 cbSize;
            public Int32 flags;
            public IntPtr hCursor;
            public POINTAPI ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINTAPI
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll")]
        static extern bool DrawIcon(IntPtr hDC, int X, int Y, IntPtr hIcon);

        const Int32 CURSOR_SHOWING = 0x00000001;


        public Action<byte[],Mat> ImageAvailable;
        public Action<Mat> ImageReceived;
        private bool captureActive = false;
        public void StartCapture()
        {
            if (captureActive) return;
            captureActive = true;
            var res = ScreenInformations.GetDisplayResolution();

            double screenLeft = SystemParameters.VirtualScreenLeft;
            double screenTop = SystemParameters.VirtualScreenTop;
            double screenWidth = res.Width;//3840; //SystemParameters.FullPrimaryScreenWidth;
            double screenHeight = res.Height;//2400; // SystemParameters.FullPrimaryScreenHeight;
            Bitmap bmp = new Bitmap((int)screenWidth, (int)screenHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);


            ImageEncodingParam[] par = new ImageEncodingParam[1];
            par[0] = new ImageEncodingParam(ImwriteFlags.WebPQuality, 80);
            Task.Run( () =>
            {
                while (captureActive)
                {
                    //await Task.Delay(10);
                    Graphics g = Graphics.FromImage(bmp);

                    g.CopyFromScreen((int)screenLeft, (int)screenTop, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);


                    CURSORINFO pci;
                    pci.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(CURSORINFO));

                    if (GetCursorInfo(out pci))
                    {
                        if (pci.flags == CURSOR_SHOWING)
                        {
                            DrawIcon(g.GetHdc(), pci.ptScreenPos.x, pci.ptScreenPos.y, pci.hCursor);
                            g.ReleaseHdc();
                        }
                    }

                    Mat mat = null;
                    if (screenWidth > 1920)
                    {
                        var resizedBmp = new Bitmap(bmp, (int)1920, (int)1080);//new Bitmap(bmp, (int)1280, (int)720);
                         mat = resizedBmp.ToMat();
                    }
                    else
                        mat=bmp.ToMat();
                   

                    var compressed = mat.ImEncode(ext: ".webp", par);

                    ImageAvailable?.Invoke(compressed, mat);
                }

            });
        }

        public void HandleNetworkImageBytes(byte[] imageBytes)
        {
            Mat decoded = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged);
            ImageReceived?.Invoke(decoded);

        }

        internal void StopCapture()
        {
            captureActive= false;
        }
    }
}

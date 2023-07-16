using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Videocall.Services.Video.H264
{
    internal class H264Transcoder
    {
        private const string DllName = "openh264-2.1.1-win32.dll";
        public static OpenH264Lib.Encoder SetupEncoder(int width, int height, OpenH264Lib.Encoder.OnEncodeCallback callback = null,
           int fps = 30, int bps = 5000_000, float keyFrameInterval = 2.0f)
        {
            OpenH264Lib.Encoder encoder = new OpenH264Lib.Encoder(DllName);

            if (callback == null)
            {
                callback = (data, length, frameType) =>
                {
                    var keyFrame = (frameType == OpenH264Lib.Encoder.FrameType.IDR) || (frameType == OpenH264Lib.Encoder.FrameType.I);

                    Console.WriteLine("Encord {0} bytes, KeyFrame:{1}", length, keyFrame);
                };
            }


            encoder.Setup(width, height, bps, 30, keyFrameInterval, callback);
            return encoder;
        }

        public static OpenH264Lib.Decoder SetupDecoder()
        {
            OpenH264Lib.Decoder decoder = new OpenH264Lib.Decoder(DllName);
            return decoder;
            //var bmp = decoder.Decode(data, length);
        }

        public static void EndoceDecodeTest()
        {
            var paths = Directory.GetFiles(@"C:\Users\dcano\Desktop\Frames");
            var decoder = SetupDecoder();
            int k = 0;
            OpenH264Lib.Encoder.OnEncodeCallback callback = (data, length, frameType) =>
            {
                var keyFrame = (frameType == OpenH264Lib.Encoder.FrameType.IDR) || (frameType == OpenH264Lib.Encoder.FrameType.I);
                var bmp = decoder.Decode(data, length);
                if (bmp != null)
                    bmp.Save(@"C:\Users\dcano\Desktop\Decoded\" + Interlocked.Increment(ref k) + ".bmp");
                //Console.WriteLine("Encord {0} bytes, KeyFrame:{1}", length, keyFrame);
            };
            var encoder = SetupEncoder(640, 480, callback);

            for (int i = 0; i < paths.Length; i++)
            {
                var frame = new Bitmap(paths[i]);
                encoder.Encode(frame);
            }
        }
    }
}

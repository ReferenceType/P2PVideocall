using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static H264Sharp.Encoder;
using H264Sharp;
namespace Videocall.Services.Video.H264
{
    internal class H264TranscoderProvider
    {
        private const string DllName64 = "openh264-2.3.1-win64.dll";
        private const string DllName32 = "openh264-2.3.1-win32.dll";
      
        public static H264Sharp.Encoder CreateEncoder(int width, int height, 
           int fps = 30, int bps = 3_000_000, ConfigType configNo=ConfigType.CameraBasic)
        {
            string ddlName = Environment.Is64BitProcess ? DllName64 : DllName32;

            H264Sharp.Encoder encoder = new H264Sharp.Encoder(ddlName);
            encoder.Initialize(width, height, bps, fps, configNo );
         
            return encoder;
        }

        public static H264Sharp.Decoder CreateDecoder()
        {
            string ddlName = Environment.Is64BitProcess ? DllName64 : DllName32;
            H264Sharp.Decoder decoder = new H264Sharp.Decoder(ddlName);
            return decoder;
        }

       
    }
}

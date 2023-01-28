using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Protection.PlayReady;
using static Videocall.MainWindow;

namespace Videocall.Services.HttpProxy
{
   
    internal class IpRetriever
    {
        public static string Uri = @"http://relayproxy.ddns.net:8001/ip";
        static IpRetriever() 
        {
            string workingDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = workingDir + "/Settings/StaticIpInfo.txt";
            if (!File.Exists(path))
            {
                Uri = "http://127.0.0.1:8001/ip";
                File.WriteAllText(path,Uri);
            }
            else
            {
                 Uri = File.ReadAllText(path);
            }

        }
        public static async Task<string> ObtainIp()
        {
            HttpClient httpClient = new HttpClient();

            var result = await httpClient.GetAsync(Uri);
            var str = await result.Content.ReadAsStreamAsync();
            byte[] bytes = new byte[4];

            str.Read(bytes, 0, 4);

            if (bytes[0] != 0 && bytes[1] != 0 && bytes[2] != 0 && bytes[3] != 0)

                return bytes[0].ToString()
                    + "." + bytes[1].ToString()
                    + "." + bytes[2].ToString()
                    + "." + bytes[3].ToString();
            return null;

        }
    }
}

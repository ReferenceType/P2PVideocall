using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Media.Protection.PlayReady;
using static Videocall.VideoCallWindow;

namespace Videocall.Services.HttpProxy
{
    public class UpdateAdress
    {
        public string Ip { get; set; }

        public string Id { get; set; }
    }
    internal class IpRetriever
    {
        static string Uri = @"http://localhost:8001/";
        public IpRetriever() { }
        public static async Task<string> ObtainIp()
        {
            try
            {
                HttpClient cl = new HttpClient();

                var uri_ = Uri;
                var result = await cl.GetAsync(uri_);
                var str = await result.Content.ReadAsStringAsync();

                var IPinfo = JsonSerializer.Deserialize<UpdateAdress>(str);
                if (IPinfo.Ip != null && IPinfo.Ip != "unknown")
                    //SettingConfig.Instance.Ip = IPinfo.Ip;
                    return IPinfo.Ip;
                return null;
            }
            catch 
            {
                throw;
                //DispatcherRun(() => { SettingsViewModel.LogText += "\nUnable To Retrieve Ip"; });
            }
        }
    }
}

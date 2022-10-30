using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Videocall
{
    public class PeerInfo
    {
        public string Name { get; }
        
        public Guid Guid { get; }

        public PeerInfo(string name, Guid guid)
        {
            Name = name;
            Guid = guid;
        }
    }
}

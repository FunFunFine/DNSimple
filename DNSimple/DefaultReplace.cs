using System.Collections.Generic;
using DNS.Protocol.ResourceRecords;

namespace DNSimple {
    public class DefaultReplace
    {
        public static DefaultReplace Empty => new DefaultReplace();
        private DefaultReplace()
        {
        }
        public static implicit operator List<IResourceRecord>(DefaultReplace _) => new List<IResourceRecord>();
    }
}
using System;
using System.Net;
using DNS.Protocol;
using DNS.Protocol.ResourceRecords;

namespace DNSimple
{
    public class ARecord : Record, IEquatable<ARecord>
    {
        public ARecord(TimeSpan timeToLive, DateTime creationTime, IPAddress ip, string domain) :
            base(timeToLive, creationTime)
        {
            Ip = ip;
            Domain = domain;
        }

        public string Domain { get; }
        public IPAddress Ip { get; }

        public bool Equals(ARecord other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return Equals(Domain, other.Domain) && Equals(Ip, other.Ip);
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            return obj is ARecord a && Equals(a);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Domain?.GetHashCode() ?? 0) * 397) ^ (Ip?.GetHashCode() ?? 0);
            }
        }

        public static implicit operator IPAddressResourceRecord(ARecord record) =>
            new IPAddressResourceRecord(new Domain(record.Domain),
                                        record.Ip,
                                        record
                                            .CreationTime +
                                        record
                                            .TimeToLive -
                                        DateTime.Now);
    }
}
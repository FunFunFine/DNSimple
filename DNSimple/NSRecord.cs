using System;
using DNS.Protocol;
using DNS.Protocol.ResourceRecords;

namespace DNSimple
{
    public class NSRecord : Record, IEquatable<NSRecord>
    {
        public NSRecord(TimeSpan timeToLive, DateTime creationTime, string domain, string nsDomain) :
            base(timeToLive, creationTime)
        {
            Domain = domain;
            NsDomain = nsDomain;
        }

        public string Domain { get; }
        public string NsDomain { get; }

        public bool Equals(NSRecord other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return Equals(Domain, other.Domain) && Equals(NsDomain, other.NsDomain);
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            return obj is NSRecord rec && Equals(rec);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Domain?.GetHashCode() ?? 0) * 397) ^ (NsDomain?.GetHashCode() ?? 0);
            }
        }

        public static implicit operator NameServerResourceRecord(NSRecord record) =>
            new NameServerResourceRecord(new Domain(record.Domain), new Domain(record.NsDomain),
                                         record.CreationTime + record.TimeToLive -
                                         DateTime.Now);
    }
}
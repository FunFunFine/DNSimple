using System;

namespace DNSimple
{
    public class Record
    {
        public Record(TimeSpan timeToLive, DateTime creationTime)
        {
            TimeToLive = timeToLive;
            CreationTime = creationTime;
        }

        public TimeSpan TimeToLive { get; }
        public DateTime CreationTime { get; }
    }
}
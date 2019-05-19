using System.Collections;
using System.Collections.Generic;

namespace DNSimple {
    public class Map<T1, T2> : IEnumerable<KeyValuePair<T1, T2>>
    {
        private readonly Dictionary<T1, T2> forward = new Dictionary<T1, T2>();
        private readonly Dictionary<T2, T1> reverse = new Dictionary<T2, T1>();

        public Map()
        {
            if (forward != null)
                Forward = new Indexer<T1, T2>(forward);
            Reverse = new Indexer<T2, T1>(reverse);
        }

        public Indexer<T1, T2> Forward { get; }
        public Indexer<T2, T1> Reverse { get; }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerator<KeyValuePair<T1, T2>> GetEnumerator() => forward.GetEnumerator();

        public bool Contains(T1 key) => forward.ContainsKey(key);

        public bool Contains(T2 key) => reverse.ContainsKey(key);

        public Map<T1, T2> Add(T1 t1, T2 t2)
        {
            forward.TryAdd(t1, t2);
            reverse.TryAdd(t2, t1);
            return this;
        }

        public class Indexer<T3, T4>
        {
            private readonly Dictionary<T3, T4> dictionary;

            public Indexer(Dictionary<T3, T4> dictionary) => this.dictionary = dictionary;

            public T4 this[T3 index]
            {
                get => dictionary[index];
                set => dictionary[index] = value;
            }

            public bool Contains(T3 key) => dictionary.ContainsKey(key);
        }
    }
}
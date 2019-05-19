using System.Collections.Generic;

namespace DNSimple
{
    public static class DictionaryExtensions
    {
        public static Dictionary<TKey, List<TValue>> Append<TKey, TValue>(
            this Dictionary<TKey, List<TValue>> dictionary,
            TKey key,
            TValue value
        )
        {
            if (dictionary.TryGetValue(key, out var list))
            {
                list.Add(value);
                dictionary[key] = list;
                return dictionary;
            }
            dictionary[key] = new List<TValue> { value };
            return dictionary;
        }
    }
}
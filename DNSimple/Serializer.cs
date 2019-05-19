using System.Collections.Generic;
using System.IO;
using LanguageExt;
using LanguageExt.SomeHelp;
using Newtonsoft.Json;

namespace DNSimple
{
    public static class Serializer
    {
        static Serializer()
        {
            Settings = new JsonSerializerSettings();
            Settings.Converters.Add(new IPAddressConverter());
            Settings.Converters.Add(new IPEndPointConverter());
        }

        public static JsonSerializerSettings Settings { get; }

        public static void Save<TRecord>(this Dictionary<string, List<TRecord>> cache, string filename)
        {
            using var file = File.CreateText(filename);
            var serializer = JsonSerializer.Create(Settings);
            //serialize object directly into file stream
            serializer.Serialize(file, cache);
        }

        public static Option<Dictionary<string, List<TRecord>>> Load<TRecord>(this string filename)
        {
            if (!File.Exists(filename))
                return Option<Dictionary<string, List<TRecord>>>.None;
            using var reader = new StreamReader(filename);
            var json = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<Dictionary<string, List<TRecord>>>(json, Settings).ToSome();
        }
    }
}
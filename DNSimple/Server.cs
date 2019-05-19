using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DNS.Protocol;
using DNS.Protocol.ResourceRecords;
using LanguageExt;
using LanguageExt.SomeHelp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using static LanguageExt.Unit;
using static LanguageExt.Option<DNS.Protocol.Response>;

namespace DNSimple
{
    public static class LoggerExtensions
    {
        public static T LogDebug<T>(this T item, string message, Logger logger)
        {
            logger.Debug(message);
            return item;
        }

        public static T LogDebug<T>(this T item, Func<T, string> message, Logger logger)
        {
            logger.Debug(message(item));
            return item;
        }
    }

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

    internal class IPEndPointConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(IPEndPoint);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var ep = (IPEndPoint) value;
            var jo = new JObject();
            jo.Add("Address", JToken.FromObject(ep.Address, serializer));
            jo.Add("Port", ep.Port);
            jo.WriteTo(writer);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            var jo = JObject.Load(reader);
            var address = jo["Address"].ToObject<IPAddress>(serializer);
            var port = (int) jo["Port"];
            return new IPEndPoint(address, port);
        }
    }

    internal class IPAddressConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(IPAddress);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer) => IPAddress.Parse((string) reader.Value);
    }

    internal class DomainConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Domain);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var domain = (Domain) value;
            writer.WriteValue(domain.ToString());
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer) => Domain.FromString((string) reader.Value);
    }

    public static class Serializer
    {
        static Serializer()
        {
            Settings = new JsonSerializerSettings();
            Settings.Converters.Add(new IPAddressConverter());
            Settings.Converters.Add(new IPEndPointConverter());
            Settings.Converters.Add(new DomainConverter());
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

    public class Server : IDisposable

    {
        private const string GoogleIp = "8.8.8.8";
        private const int Port = 53;
        const string PathPrefix = @".\..\..\..\";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly UdpClient client = new UdpClient(new IPEndPoint(IPAddress.Any, Port));

        public Dictionary<string, List<ARecord>> ACache;
        public Dictionary<string, List<NSRecord>> NsCache;

        public Server()
        {
            (PathPrefix + nameof(ACache))
                .Load<ARecord>()
                .Bind(ac => (PathPrefix + nameof(NsCache)).Load<NSRecord>().Map(nsc => (ac, nsc)))
                .Do(t => (ACache, NsCache) = t)
                .IfNone(() => (ACache, NsCache) = (new Dictionary<string, List<ARecord>>(),
                                                   new Dictionary<string, List<NSRecord>>()).LogDebug(_=>"Creating default cache",Logger));
            //.IfNone(() => throw new FileLoadException());
        }

        public void Run()
        {
            ConfigLogger();

            Logger.Debug($"Starting DNS server at {Port}");
            while (true)
            {
                try
                {
                    var (request, sender) = GetRequest().Result;

                    var settings = new JsonSerializerSettings();
                    settings.Converters.Add(new IPAddressConverter());
                    settings.Converters.Add(new IPEndPointConverter());
                    settings.Converters.Add(new DomainConverter());

                    var json = JsonConvert.SerializeObject(ACache, settings);

                    var domain = JsonConvert.SerializeObject(new Domain("google.com"), settings);
                    var d = JsonConvert.DeserializeObject<Domain>(domain, settings);

                    var dict = JsonConvert.DeserializeObject<Dictionary<string, List<ARecord>>>(json, settings);
                    (request.Questions.First()
                            .LogDebug(q => $"Got DNS request #{request.Id} from {sender} for {q}", Logger) switch
                         {
                         { Type: RecordType.NS } ns1 => ProcessRequest(ns1, request, NsCache, record =>
                                                                           (NameServerResourceRecord) record),
                         { Type: RecordType.A } a1 => ProcessRequest(a1, request, ACache,
                                                                     record => (IPAddressResourceRecord) record),
                         _ => None
                         }).BiMap(Default.Return, () => Resolve(request).Result)
                           .LogDebug(a => $"Got an answer to #{request.Id} for {sender}: {a}", Logger)
                           .Do(r => SendAnswer(r, sender).Wait());
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    
                }
            }
        }

        private Option<Response> ProcessRequest<TRecord>(
            Question question,
            Request request,
            Dictionary<string, List<TRecord>> cache,
            Func<TRecord, IResourceRecord> creator)
            where TRecord : Record
        {
            if (!cache.TryGetValue(question.Name.ToString(), out var records) ||
                !records.Any(record => record.CreationTime + record.TimeToLive > DateTime.Now))
            {
                Logger.Info("\nCould not find request in cache!\n");
                return None.LogDebug(_=>"\nCould not find request in cache!\n",Logger);
            }
            cache[question.Name.ToString()] =
                records.Where(record => record.CreationTime + record.TimeToLive > DateTime.Now).ToList();
            return new
                Response(new Header
                         {
                             AdditionalRecordCount = 0,
                             AnswerRecordCount = 1,
                             OperationCode = request.OperationCode,
                             QuestionCount = 1,
                             Id = request.Id,
                             Response = true,
                             RecursionDesired = request.RecursionDesired
                         },
                         new List<Question> { question }, records
                                                          .Where(record => record.CreationTime + record.TimeToLive >
                                                                           DateTime.Now)
                                                          .Select(creator)
                                                          .ToList()
                         ,
                         new List<IResourceRecord>(), new List<IResourceRecord>());
        }

        private static void ConfigLogger()
        {
            var config = new LoggingConfiguration();
            var logconsole = new ConsoleTarget("logconsole");

            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logconsole);

            LogManager.Configuration = config;
        }

        public async Task SendAnswer(Response answer, IPEndPoint endPoint)
        {
            Logger.Debug($"Sending answers to {endPoint}: {answer}");
            await client.SendAsync(answer.ToArray(), answer.Size, endPoint);
        }

        private async Task<Response> Resolve(Request request)
        {
            using var udp = new UdpClient();
            var google = new IPEndPoint(IPAddress.Parse(GoogleIp), 53);

            await udp.SendAsync(request.ToArray(), request.Size, google);
            var result = await udp.ReceiveAsync();

            var response = Response.FromArray(result.Buffer);
            AddToCache(response);
            return response;
        }

        private void AddToCache(Response response)
        {
            foreach (var record in response.AnswerRecords
                                           .Concat(response.AdditionalRecords)
                                           .Concat(response.AuthorityRecords))
            {
                switch (record.Type)
                {
                    case RecordType.A:
                        var ipRecord = (IPAddressResourceRecord) record;
                        ACache.Append(ipRecord.Name.ToString(),
                                      new ARecord(ipRecord.TimeToLive, DateTime.Now, ipRecord.IPAddress,
                                                  ipRecord.Name.ToString()));
                        break;
                    case RecordType.CNAME:
                        var cNameRecord = (CanonicalNameResourceRecord)record;
                        if (ACache.TryGetValue(cNameRecord.CanonicalDomainName.ToString(), out var data))
                            ACache[cNameRecord.Name.ToString()] = data;
                        break;
                    case RecordType.NS:
                        var nsRecord = (NameServerResourceRecord) record;
                        NsCache.Append(nsRecord.Name.ToString(),
                                       new NSRecord(nsRecord.TimeToLive, DateTime.Now, nsRecord.Name.ToString(),
                                                    nsRecord.NSDomainName.ToString()));
                        break;
                    default:
                        continue;
                }
            }
            ReleaseUnmanagedResources();
        }

        private async Task<(Request request, IPEndPoint sender)> GetRequest()
        {
            var data = await client.ReceiveAsync();

            return (Request.FromArray(data.Buffer), data.RemoteEndPoint);
        }

        private void ReleaseUnmanagedResources()
        {
            ACache.Save(PathPrefix+nameof(ACache));
            NsCache.Save(PathPrefix+nameof(NsCache));
        }

        private void Dispose(bool disposing)
        {
            ReleaseUnmanagedResources();
            if (disposing)
            {
                client?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~Server() {
            Dispose(false);
        }
    }
}
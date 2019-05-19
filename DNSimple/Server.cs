using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DNS.Protocol;
using DNS.Protocol.ResourceRecords;
using LanguageExt;
using Newtonsoft.Json;
using NLog;
using NLog.Config;
using NLog.Targets;
using static LanguageExt.Unit;
using static LanguageExt.Option<DNS.Protocol.Response>;

namespace DNSimple
{
    public class Server : IDisposable

    {
        private const string GoogleIp = "8.8.8.8";
        private const int Port = 53;
        private const string PathPrefix = @".\..\..\..\";

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
                                                   new Dictionary<string, List<NSRecord>>())
                              .LogDebug(_ => "Creating default cache", Logger));
            //.IfNone(() => throw new FileLoadException());
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
                return None;
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
                        var cNameRecord = (CanonicalNameResourceRecord) record;
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
            ACache.Save(PathPrefix + nameof(ACache));
            NsCache.Save(PathPrefix + nameof(NsCache));
        }

        private void Dispose(bool disposing)
        {
            ReleaseUnmanagedResources();
            if (disposing)
                client?.Dispose();
        }

        ~Server()
        {
            Dispose(false);
        }
    }
}
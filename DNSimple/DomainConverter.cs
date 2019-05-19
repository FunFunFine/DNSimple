using System;
using DNS.Protocol;
using Newtonsoft.Json;

namespace DNSimple
{
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
}
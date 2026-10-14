using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CatMiners.Core.Outpost
{
    /// <summary>
    /// Сериализация режима CatOutpostV2: Newtonsoft без TypeNameHandling, BigInteger — десятичная строка,
    /// Rational — "n/d", enum — имена, словари со строковыми ключами — по возрастанию ключа (ordinal),
    /// поэтому одинаковое состояние всегда даёт побайтно одинаковый JSON.
    /// </summary>
    public static class OutpostJson
    {
        static readonly UTF8Encoding s_Utf8 = new UTF8Encoding(false);

        public static readonly JsonSerializerSettings Settings = CreateSettings();

        public static JsonSerializerSettings CreateSettings()
        {
            var s = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                Culture = CultureInfo.InvariantCulture,
                FloatFormatHandling = FloatFormatHandling.String,
                FloatParseHandling = FloatParseHandling.Double,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                TypeNameHandling = TypeNameHandling.None,
                DateParseHandling = DateParseHandling.None,
            };
            s.Converters.Add(new BigIntegerStringConverter());
            s.Converters.Add(new RationalStringConverter());
            s.Converters.Add(new StringEnumConverter());
            s.Converters.Add(new SortedKeysDictionaryConverter());
            return s;
        }

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        /// <summary>Глубокая копия через сериализацию (офлайн и переход работают на копии).</summary>
        public static T DeepCopy<T>(T value) where T : class => value == null ? null : Deserialize<T>(Serialize(value));

        public static string Sha256Hex(string text) => Sha256Hex(s_Utf8.GetBytes(text));

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(bytes);
                var sb = new StringBuilder(digest.Length * 2);
                foreach (var b in digest) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static byte[] Utf8(string text) => s_Utf8.GetBytes(text);
    }

    /// <summary>BigInteger ⇄ десятичная строка (без потери единицы выше 2^53).</summary>
    public sealed class BigIntegerStringConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(BigInteger) || objectType == typeof(BigInteger?);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null) writer.WriteNull();
            else writer.WriteValue(((BigInteger)value).ToString(CultureInfo.InvariantCulture));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Null:
                    if (objectType == typeof(BigInteger?)) return null;
                    return BigInteger.Zero;
                case JsonToken.String:
                    return MoneyFormat.Parse((string)reader.Value);
                case JsonToken.Integer:
                    if (reader.Value is BigInteger big) return big;
                    return new BigInteger(Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture));
                default:
                    throw new JsonSerializationException("BigInteger: неожиданный токен " + reader.TokenType + " в " + reader.Path);
            }
        }
    }

    /// <summary>Rational ⇄ "n/d" (или "n"); читает также десятичную запись и целые числа JSON.</summary>
    public sealed class RationalStringConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Rational) || objectType == typeof(Rational?);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null) writer.WriteNull();
            else writer.WriteValue(((Rational)value).ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Null:
                    if (objectType == typeof(Rational?)) return null;
                    throw new JsonSerializationException("Rational: null в " + reader.Path);
                case JsonToken.String:
                    if (!Rational.TryParse((string)reader.Value, out var r))
                        throw new JsonSerializationException("Rational: неверная запись \"" + reader.Value + "\" в " + reader.Path);
                    return r;
                case JsonToken.Integer:
                    if (reader.Value is BigInteger big) return new Rational(big);
                    return Rational.FromLong(Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture));
                default:
                    throw new JsonSerializationException("Rational: ожидалась строка дроби, найден " + reader.TokenType + " в " + reader.Path);
            }
        }
    }

    /// <summary>
    /// Dictionary&lt;string, T&gt;: запись по возрастанию ключа (ordinal), чтение в обычный Dictionary.
    /// Порядок перечисления словаря не влияет ни на JSON, ни на hash.
    /// </summary>
    public sealed class SortedKeysDictionaryConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
            objectType.GetGenericArguments()[0] == typeof(string);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            var dict = (IDictionary)value;
            var keys = new List<string>(dict.Count);
            foreach (var k in dict.Keys) keys.Add((string)k);
            keys.Sort(StringComparer.Ordinal);
            writer.WriteStartObject();
            foreach (var k in keys)
            {
                writer.WritePropertyName(k);
                serializer.Serialize(writer, dict[k]);
            }
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            if (reader.TokenType != JsonToken.StartObject)
                throw new JsonSerializationException("Словарь: ожидался объект в " + reader.Path);
            var valueType = objectType.GetGenericArguments()[1];
            var dict = (IDictionary)Activator.CreateInstance(objectType, (object)StringComparer.Ordinal);
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject) return dict;
                if (reader.TokenType != JsonToken.PropertyName)
                    throw new JsonSerializationException("Словарь: ожидалось имя ключа в " + reader.Path);
                string key = (string)reader.Value;
                if (!reader.Read()) break;
                if (dict.Contains(key)) throw new JsonSerializationException("Словарь: повтор ключа " + key + " в " + reader.Path);
                dict[key] = serializer.Deserialize(reader, valueType);
            }
            throw new JsonSerializationException("Словарь: неожиданный конец JSON");
        }
    }
}

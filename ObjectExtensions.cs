using CsvHelper;
using CsvHelper.Configuration;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text.Json;
using System.Xml.Serialization;

namespace TransforMagiX
{

    /// <summary>
    /// Provides extension methods for various object serialization and utility functions.
    /// </summary>
    public static class ObjectExtensions
    {
        private static readonly BindingFlags PropertyFlags = BindingFlags.Public | BindingFlags.Instance;
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache =
        new ConcurrentDictionary<Type, PropertyInfo[]>();
        private static readonly SerializationConfiguration DefaultConfig = new SerializationConfiguration();

        private static PropertyInfo[] GetCachedProperties(Type type)
        {
            return PropertyCache.GetOrAdd(type, t =>
                t.GetProperties(PropertyFlags));
        }

        /// <summary>
        /// Validates input size against configuration limits.
        /// </summary>
        /// <param name="input">The input string to validate.</param>
        /// <param name="config">The serialization configuration.</param>
        private static void ValidateInput(string input, SerializationConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(input);

            if (input.Length > config.MaxInputLength)
            {
                throw new ArgumentException($"Input exceeds maximum length of {config.MaxInputLength} characters");
            }
        }

        /// <summary>
        /// Executes an operation with retry logic based on configuration.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="operation">The operation to execute.</param>
        /// <param name="config">The serialization configuration.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the operation.</returns>
        private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, SerializationConfiguration config, CancellationToken cancellationToken)
        {
            var attempts = 0;
            while (true)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempts < config.MaxRetries &&
                                        !(ex is ArgumentException ||
                                          ex is SecurityException ||
                                          ex is OperationCanceledException))
                {
                    attempts++;
                    await Task.Delay(config.RetryDelay * attempts, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Compresses a string using GZip compression.
        /// </summary>
        /// <param name="input">The input string to compress.</param>
        /// <returns>A byte array containing the compressed data.</returns>
        private static async Task<byte[]> CompressStringAsync(string input)
        {
            using var memoryStream = new MemoryStream();
            using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal))
            using (var writer = new StreamWriter(gzipStream))
            {
                await writer.WriteAsync(input);
            }
            return memoryStream.ToArray();
        }

        /// <summary>
        /// Decompresses a byte array using GZip decompression.
        /// </summary>
        /// <param name="compressed">The compressed byte array.</param>
        /// <returns>The decompressed string.</returns>
        private static async Task<string> DecompressBytesAsync(byte[] compressed)
        {
            using var inputStream = new MemoryStream(compressed);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzipStream);
            return await reader.ReadToEndAsync();
        }

        /// <summary>
        /// Serializes an object to a JSON string.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="options">JSON serialization options.</param>
        /// <returns>A JSON string representation of the object.</returns>
        public static string ToJson(this object obj, JsonSerializerOptions options = null)
        {
            return JsonSerializer.Serialize(obj, options);
        }

        /// <summary>
        /// Serializes an object to a JSON string with advanced options.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="options">JSON serialization options.</param>
        /// <param name="config">Serialization configuration options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A JSON string representation of the object.</returns>
        public static async Task<string> ToJsonAsync<T>(this T obj,
            JsonSerializerOptions options = null,
            SerializationConfiguration config = null,
            CancellationToken cancellationToken = default)
        {
            config ??= DefaultConfig;

            try
            {
                return await ExecuteWithRetryAsync(async () =>
                {
                    using var stream = new MemoryStream();
                    await using var writer = new Utf8JsonWriter(stream);

                    options ??= new JsonSerializerOptions
                    {
                        MaxDepth = config.MaxDepth,
                        WriteIndented = true
                    };

                    await JsonSerializer.SerializeAsync(stream, obj, options, cancellationToken);

                    if (config.EnableCompression)
                    {
                        var compressed = await CompressStringAsync(Convert.ToBase64String(stream.ToArray()));
                        return Convert.ToBase64String(compressed);
                    }

                    stream.Position = 0;
                    using var reader = new StreamReader(stream);
                    return await reader.ReadToEndAsync();
                }, config, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new JsonDeserializationException("Error serializing to JSON.", ex);
            }
        }

        /// <summary>
        /// Deserializes a JSON string to an object of type T.
        /// </summary>
        /// <typeparam name="T">The type of the object to deserialize to.</typeparam>
        /// <param name="json">The JSON string to deserialize.</param>
        /// <param name="options">JSON deserialization options.</param>
        /// <returns>An object of type T.</returns>
        /// <exception cref="JsonDeserializationException">Thrown when an error occurs during JSON deserialization.</exception>
        public static T FromJson<T>(this string json, JsonSerializerOptions options = null)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, options);
            }
            catch (JsonException ex)
            {
                throw new JsonDeserializationException("Error deserializing JSON.", ex);
            }
        }

        /// <summary>
        /// Deserializes a JSON string to an object of type T with advanced options.
        /// </summary>
        /// <typeparam name="T">The type of the object to deserialize to.</typeparam>
        /// <param name="json">The JSON string to deserialize.</param>
        /// <param name="options">JSON deserialization options.</param>
        /// <param name="config">Serialization configuration options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An object of type T.</returns>
        /// <exception cref="JsonDeserializationException">Thrown when an error occurs during JSON deserialization.</exception>
        public static async Task<T> FromJsonAsync<T>(this string json,
            JsonSerializerOptions options = null,
            SerializationConfiguration config = null,
            CancellationToken cancellationToken = default)
        {
            config ??= DefaultConfig;
            ValidateInput(json, config);

            try
            {
                return await ExecuteWithRetryAsync(async () =>
                {
                    string workingJson = json;
                    if (config.EnableCompression)
                    {
                        var compressed = Convert.FromBase64String(json);
                        workingJson = await DecompressBytesAsync(compressed);
                    }

                    using var stream = new MemoryStream();
                    using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(workingJson);
                    await writer.FlushAsync();
                    stream.Position = 0;

                    var result = await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken);
                    return result;
                }, config, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new JsonDeserializationException("Error deserializing JSON.", ex);
            }
        }

        /// <summary>
        /// Serializes an object to an XML string.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="namespaces">XML namespaces to use.</param>
        /// <param name="rootElementName">The name of the root XML element. If null, the type name is used.</param>
        /// <returns>An XML string representation of the object.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the object to serialize is null.</exception>
        public static string ToXml(this object obj, XmlSerializerNamespaces namespaces = null, string rootElementName = null)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), "Object to serialize cannot be null.");
            }

            var type = obj.GetType();
            var serializer = string.IsNullOrEmpty(rootElementName) ? new XmlSerializer(type) : new XmlSerializer(type, new XmlRootAttribute(rootElementName));

            if (namespaces == null)
            {
                namespaces = new XmlSerializerNamespaces();
                namespaces.Add(string.Empty, string.Empty); // Remove default namespaces
            }

            using var stringWriter = new StringWriter();
            serializer.Serialize(stringWriter, obj, namespaces);
            return stringWriter.ToString();
        }

        /// <summary>
        /// Serializes an object to an XML string.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="namespaces">XML namespaces to use.</param>
        /// <param name="rootElementName">The name of the root XML element. If null, the type name is used.</param>
        /// <returns>An XML string representation of the object.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the object to serialize is null.</exception>
        public static string ConvertToXml(object obj, XmlSerializerNamespaces namespaces = null, string rootElementName = null)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), "Object to serialize cannot be null.");
            }

            var type = obj.GetType();
            var serializer = string.IsNullOrEmpty(rootElementName)
                ? new XmlSerializer(type)
                : new XmlSerializer(type, new XmlRootAttribute(rootElementName));

            if (namespaces == null)
            {
                namespaces = new XmlSerializerNamespaces();
                namespaces.Add(string.Empty, string.Empty); // Remove default namespaces
            }

            using (var stringWriter = new StringWriter())
            {
                serializer.Serialize(stringWriter, obj, namespaces);
                return stringWriter.ToString();
            }
        }

        /// <summary>
        /// Deserializes an XML string to an object of type T.
        /// </summary>
        /// <typeparam name="T">The type of the object to deserialize to.</typeparam>
        /// <param name="xml">The XML string to deserialize.</param>
        /// <returns>An object of type T.</returns>
        /// <exception cref="XmlDeserializationException">Thrown when an error occurs during XML deserialization.</exception>
        public static T FromXml<T>(this string xml)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(T));
                using var stringReader = new StringReader(xml);
                return (T)serializer.Deserialize(stringReader);
            }
            catch (InvalidOperationException ex)
            {
                throw new XmlDeserializationException("Error deserializing XML.", ex);
            }
        }

        /// <summary>
        /// Serializes an object to a comma separated string.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>A CSV string representation of the object.</returns>
        public static string ToCommaSeparated(this object obj)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            var properties = GetCachedProperties(obj.GetType()); // Static properties are usually not part of CSV

            if (properties.Length == 0)
            {
                return string.Empty; // Or handle differently if needed
            }

            string[] values = new string[properties.Length];
            for (int i = 0; i < properties.Length; i++)
            {
                var value = properties[i].GetValue(obj);

                string valueString;
                switch (value)
                {
                    case null:
                        valueString = string.Empty;
                        break;
                    case bool b:
                        valueString = b.ToString().ToLowerInvariant();
                        break;
                    case string s:
                        valueString = $"{EscapeCsvValue(s)}"; // Escape special characters in strings
                        break;
                    default:
                        valueString = value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : string.Empty;
                        break;
                }
                values[i] = valueString;
            }

            return string.Join(",", values);
        }

        /// <summary>
        /// Escapes special characters in a CSV value.
        /// </summary>
        /// <param name="s">The string to escape.</param>
        /// <returns>The escaped string.</returns>
        private static string EscapeCsvValue(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            {
                return $"\"{s.Replace("\"", "\"\"")}\""; // Escape quotes by doubling them
            }
            return s;
        }

        /// <summary>
        /// Serializes an object to a CSV string.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="delimiter">The CSV delimiter (default is comma).</param>
        /// <param name="includeHeader">Whether to include a header row with property names (default is true).</param>
        /// <returns>A CSV string representation of the object.</returns>
        public static string ToCsv<T>(this T obj, string delimiter = ",", bool includeHeader = true)
        {
            if (obj == null) return string.Empty;

            if (obj is IEnumerable enumerable && !(obj is string)) // Exclude strings from IEnumerable handling
            {

                var itemType = GetItemType(enumerable);
                var castMethod = typeof(Enumerable).GetMethod("Cast").MakeGenericMethod(itemType);
                var castedEnumerable = castMethod.Invoke(null, new object[] { enumerable });

                var toCsvMethod = typeof(ObjectExtensions)
                    .GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == nameof(ToCsv)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters()[0].ParameterType.IsGenericType
                        && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>));

                toCsvMethod = toCsvMethod?.MakeGenericMethod(itemType);

                return (string)toCsvMethod.Invoke(null, new object[] { castedEnumerable, delimiter, includeHeader });

            }
            else
            {
                var list = new[] { obj };
                return list.ToCsv(delimiter, includeHeader);
            }

        }

        /// <summary>
        /// Gets the type of the items in an IEnumerable, even if it's non-generic.
        /// Handles mixed collections by returning null.
        /// </summary>
        private static Type GetItemType(IEnumerable enumerable)
        {
            if (enumerable == null) return null;

            foreach (var item in enumerable)
            {
                if (item != null)  // Handle nulls within the collection
                {
                    return item.GetType();
                }
            }

            return null; // Could not determine type.  Likely an empty collection.
        }

        /// <summary>
        /// Serializes a collection of objects to a CSV string.
        /// </summary>
        /// <typeparam name="T">The type of the objects in the collection.</typeparam>
        /// <param name="objects">The collection of objects to serialize.</param>
        /// <param name="delimiter">The CSV delimiter (default is comma).</param>
        /// <param name="includeHeader">Whether to include a header row with property names (default is true).</param>
        /// <returns>A CSV string representation of the objects.</returns>
        public static string ToCsv<T>(this IEnumerable<T> objects, string delimiter = ",", bool includeHeader = true)
        {
            if (objects == null) return string.Empty;

            using var stringWriter = new StringWriter();
            using var csvWriter = new CsvWriter(stringWriter, CultureInfo.InvariantCulture);
            csvWriter.Context.TypeConverterCache.AddConverter<bool>(new LowerCaseBooleanConverter());
            csvWriter.Context.Configuration.Delimiter = delimiter;

            if (includeHeader)
            {
                if (!IsEnumerable(typeof(T)))
                {
                    csvWriter.WriteHeader<T>();
                }
                else
                {
                    var containingType = GetEnumerableGenericType(objects);
                    csvWriter.WriteHeader(containingType); // Writes header based on properties of the enumerable containing type
                }

                csvWriter.NextRecord();
            }

            foreach (var obj in objects)
            {
                csvWriter.WriteRecord(obj);
                csvWriter.NextRecord();
            }

            return stringWriter.ToString();
        }

        /// <summary>
        /// Serializes a collection of objects to CSV format with advanced options and batching support.
        /// </summary>
        /// <typeparam name="T">The type of the objects in the collection.</typeparam>
        /// <param name="objects">The collection of objects to serialize.</param>
        /// <param name="config">Serialization configuration options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A CSV string representation of the objects.</returns>
        /// <exception cref="Exception">Thrown when an error occurs during CSV serialization.</exception>
        public static async Task<string> ToCsvAsync<T>(this IEnumerable<T> objects,
            SerializationConfiguration config = null,
            CancellationToken cancellationToken = default)
        {
            config ??= DefaultConfig;

            try
            {
                return await ExecuteWithRetryAsync(async () =>
                {
                    using var stringWriter = new StringWriter();
                    using var csv = new CsvWriter(stringWriter, config.Culture);
                    csv.Context.TypeConverterCache.AddConverter<bool>(new LowerCaseBooleanConverter());
                    // Write header
                    csv.WriteHeader<T>();
                    await csv.NextRecordAsync();

                    // Process in batches
                    var enumerator = objects.GetEnumerator();
                    var batch = new List<T>(config.BatchSize);

                    while (true)
                    {
                        batch.Clear();
                        for (int i = 0; i < config.BatchSize && enumerator.MoveNext(); i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            batch.Add(enumerator.Current);
                        }

                        if (batch.Count == 0) break;

                        foreach (var item in batch)
                        {
                            csv.WriteRecord(item);
                            await csv.NextRecordAsync();
                        }
                    }

                    var result = stringWriter.ToString();
                    if (config.EnableCompression)
                    {
                        var compressed = await CompressStringAsync(result);
                        return Convert.ToBase64String(compressed);
                    }

                    return result;
                }, config, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new Exception("Error serializing to CSV.", ex);
            }
        }

        /// <summary>
        /// Deserializes a CSV string to a list of objects of type T.
        /// </summary>
        /// <typeparam name="T">The type of the objects to deserialize to.</typeparam>
        /// <param name="csv">The CSV string to deserialize.</param>
        /// <param name="delimiter">The CSV delimiter (default is comma).</param>
        /// <param name="hasHeaderRecord">Indicates if the CSV has a header record.</param>
        /// <returns>A list of objects of type T.</returns>
        public static List<T> FromCsv<T>(this string csv, string delimiter = ",", bool hasHeaderRecord = false) where T : new()
        {
            using var stringReader = new StringReader(csv);
            using var csvReader = new CsvReader(stringReader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = delimiter,
                HasHeaderRecord = hasHeaderRecord
            });

            if (!hasHeaderRecord)
            {
                var properties = GetCachedProperties(typeof(T));
                var records = new List<T>();

                while (csvReader.Read())
                {
                    var record = new T();
                    for (int i = 0; i < properties.Length; i++)
                    {
                        var property = properties[i];
                        var value = csvReader.GetField(i);
                        property.SetValue(record, Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture));
                    }
                    records.Add(record);
                }
                return records;
            }

            return csvReader.GetRecords<T>().ToList();
        }

        /// <summary>
        /// Deserializes a CSV string to a list of objects with advanced options and batching support.
        /// </summary>
        /// <typeparam name="T">The type of the objects to deserialize to.</typeparam>
        /// <param name="csv">The CSV string to deserialize.</param>
        /// <param name="config">Serialization configuration options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A list of objects of type T.</returns>
        /// <exception cref="Exception">Thrown when an error occurs during CSV deserialization.</exception>
        public static async Task<List<T>> FromCsvAsync<T>(this string csv,
            SerializationConfiguration config = null,
            CancellationToken cancellationToken = default) where T : new()
        {
            config ??= DefaultConfig;
            ValidateInput(csv, config);

            try
            {
                return await ExecuteWithRetryAsync(async () =>
                {
                    string workingCsv = csv;
                    if (config.EnableCompression)
                    {
                        var compressed = Convert.FromBase64String(csv);
                        workingCsv = await DecompressBytesAsync(compressed);
                    }

                    using var stringReader = new StringReader(workingCsv);
                    using var csvReader = new CsvReader(stringReader, config.Culture);
                    var records = new List<T>();

                    await foreach (var record in csvReader.GetRecordsAsync<T>().WithCancellation(cancellationToken))
                    {
                        records.Add(record);
                    }

                    return records;
                }, config, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new Exception("Error deserializing CSV.", ex);
            }
        }

        /// <summary>
        /// Gets the property names of an object.
        /// </summary>
        /// <param name="obj">The object to get property names from.</param>
        /// <returns>An array of property names.</returns>
        public static string[] GetPropertyNames(this object obj)
        {
            if (obj == null) return Array.Empty<string>();
            return GetCachedProperties(obj.GetType()).Select(p => p.Name).ToArray();
        }

        /// <summary>
        /// Automatically generates a string representation of an object.
        /// </summary>
        /// <param name="obj">The object to generate a string representation for.</param>
        /// <returns>A string representation of the object.</returns>
        public static string AutoGenerateToString<T>(this T obj)
        {
            if (obj == null)
                return string.Empty;

            var properties = GetCachedProperties(obj.GetType());

            return string.Join(", ", properties.Select(p =>
            {
                var value = p.GetValue(obj);
                string valueString;

                switch (value)
                {
                    case null:
                        valueString = "null";
                        break;
                    case bool b:
                        valueString = b.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
                        break;
                    case string s:
                        valueString = $"\"{s?.Replace("\"", "\\\"")}\"";
                        break;
                    default:
                        valueString = Convert.ToString(value, CultureInfo.InvariantCulture);
                        break;
                }

                return $"{p.Name} = {valueString}";
            }));
        }

        public static bool IsEnumerable(object obj)
        {
            return obj.GetType().GetInterfaces()
                .Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        }

        public static Type GetEnumerableGenericType(object obj)
        {
            var enumerableType = obj.GetType().GetInterfaces()
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableType?.GenericTypeArguments[0];
        }

        // Writes CSV for a collection to disk (synchronous).
        public static void WriteCsvToFile<T>(this IEnumerable<T> objects, string path, string delimiter = ",", bool includeHeader = true)
        {
            ArgumentNullException.ThrowIfNull(objects);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var csv = objects.ToCsv(delimiter, includeHeader) ?? string.Empty;
            File.WriteAllText(path, csv, System.Text.Encoding.UTF8);
        }

        // Writes CSV for a collection to disk (async, uses existing ToCsvAsync and supports config/cancellation).
        public static async Task WriteCsvToFileAsync<T>(this IEnumerable<T> objects, string path, SerializationConfiguration config = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(objects);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var csv = await objects.ToCsvAsync(config, cancellationToken).ConfigureAwait(false) ?? string.Empty;
            await File.WriteAllTextAsync(path, csv, System.Text.Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        // Writes JSON for an object to disk (synchronous).
        public static void WriteJsonToFile<T>(this T obj, string path, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(obj);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(obj, options);
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }

        // Writes JSON for an object to disk (async). Respects SerializationConfiguration when provided
        // by delegating to existing ToJsonAsync (so compression, retries, etc. are honored).
        public static async Task WriteJsonToFileAsync<T>(this T obj, string path, JsonSerializerOptions options = null, SerializationConfiguration config = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(obj);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = await obj.ToJsonAsync(options, config, cancellationToken).ConfigureAwait(false) ?? string.Empty;
            await File.WriteAllTextAsync(path, json, System.Text.Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        // Writes XML for an object to disk (synchronous).
        public static void WriteXmlToFile(this object obj, string path, XmlSerializerNamespaces namespaces = null, string rootElementName = null)
        {
            ArgumentNullException.ThrowIfNull(obj);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var xml = obj.ToXml(namespaces, rootElementName);
            File.WriteAllText(path, xml, System.Text.Encoding.UTF8);
        }

        // Writes XML for an object to disk (async).
        public static async Task WriteXmlToFileAsync(this object obj, string path, XmlSerializerNamespaces namespaces = null, string rootElementName = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(obj);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // ToXml is synchronous; call it then write asynchronously.
            var xml = obj.ToXml(namespaces, rootElementName);
            await File.WriteAllTextAsync(path, xml, System.Text.Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
    }
}

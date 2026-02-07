# TransforMagiX

# TransforMagiX

TransforMagiX is a small .NET 8 library of serialization helpers and utilities that simplify common data transformation tasks across services and tooling. It provides high-level extension methods for JSON, XML and CSV serialization/deserialization, optional GZip compression, retry logic and small reflection utilities.

## Key features
- JSON: `ToJson`, `ToJsonAsync`, `FromJson`, `FromJsonAsync`
- XML: `ToXml`, `FromXml`
- CSV: `ToCsv`, `ToCsvAsync`, `FromCsv`, `FromCsvAsync` (single objects and collections)
- Optional GZip compression + Base64 encoding via `SerializationConfiguration`
- Configurable retry/backoff and input validation
- Reflection property caching for performance (`PropertyCache`)
- Utilities: `GetPropertyNames`, `AutoGenerateToString`, `ToCommaSeparated`

## Installation
- Add the project to your solution or reference the package (if published).
- Target framework: .NET 8 (library compiled for .NET 8).

## Quick start

Configure behavior with `SerializationConfiguration`:

using TransforMagiX; using System.Globalization;
var config = new SerializationConfiguration { MaxDepth = 32, EnableCompression = false, MaxRetries = 3, RetryDelay = TimeSpan.FromMilliseconds(100), MaxInputLength = 1_000_000, Culture = CultureInfo.InvariantCulture, BatchSize = 100 };

JSON examples:

var obj = new MyType { /* ... */ };

// sync 
string json = obj.ToJson();

// async w/ config 
string jsonAsync = await obj.ToJsonAsync(options: null, config: config);

// deserialize 
var deserialized = json.FromJson<MyType>(); var deserializedAsync = await jsonAsync.FromJsonAsync<MyType>(options: null, config: config);


CSV examples:

// single object -> CSV line 
string line = obj.ToCommaSeparated();

// collection -> CSV (sync) 
string csv = myCollection.ToCsv();

// collection -> CSV (async, batching/compression via config) 
string csvAsync = await myCollection.ToCsvAsync(config);

// parse CSV 
var items = csv.FromCsv<MyType>(hasHeaderRecord: true); 
var itemsAsync = await csvAsync.FromCsvAsync<MyType>(config);


XML examples:
string xml = obj.ToXml();             // serialize var 
fromXml = xml.FromXml<MyType>(); // deserialize

Utilities:

string[] names = obj.GetPropertyNames(); 
string summary = obj.AutoGenerateToString();

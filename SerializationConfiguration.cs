using System.Globalization;

namespace TransforMagiX
{
    /// <summary>
    /// Configuration options for serialization operations.
    /// </summary>
    public class SerializationConfiguration
    {
        public int MaxDepth { get; set; } = 32;
        public bool EnableCompression { get; set; } = false;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
        public int MaxRetries { get; set; } = 3;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public long MaxInputLength { get; set; } = 100 * 1024 * 1024; // 100MB default
        public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;
        public int BatchSize { get; set; } = 1000;
    }
}

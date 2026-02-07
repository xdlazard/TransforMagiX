namespace TransforMagiX
{
    /// <summary>
    /// Metadata for serialization operations.
    /// </summary>
    [Serializable]
    public class SerializationMetadata
    {
        public Version Version { get; set; }
        public DateTime SerializedAt { get; set; }
        public string SerializedBy { get; set; }
        public Dictionary<string, string> CustomProperties { get; set; }
    }
}

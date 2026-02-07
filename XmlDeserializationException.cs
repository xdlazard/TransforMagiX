namespace TransforMagiX
{
    /// <summary>
    /// Exception thrown when an error occurs during XML deserialization.
    /// </summary>
    public class XmlDeserializationException : Exception
    {
        public XmlDeserializationException(string message, Exception innerException) : base(message, innerException) { }
    }
}

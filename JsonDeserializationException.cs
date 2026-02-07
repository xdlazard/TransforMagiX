namespace TransforMagiX
{
    /// <summary>
    /// Exception thrown when an error occurs during JSON deserialization.
    /// </summary>
    public class JsonDeserializationException : Exception
    {
        public JsonDeserializationException(string message, Exception innerException) : base(message, innerException) { }
    }
}

namespace FlRockBand3
{
    public class MessageHandlerArgs
    {
        public enum MessageType
        {
            Info,
            Warning,
            Error
        }

        public MessageHandlerArgs(MessageType type, string message)
        {
            Message = message;
            Type = type;
        }

        public MessageType Type { get; }
        public string Message { get; }
    }
}
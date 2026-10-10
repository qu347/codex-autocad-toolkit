namespace AiCad.Bridge;

public sealed record BridgeOptions(string SessionDirectory, TimeSpan RpcTimeout, int MaxMessageBytes)
{
    public const int DefaultMaxMessageBytes = 1024 * 1024;

    public static BridgeOptions CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new BridgeOptions(
            Path.Combine(localAppData, "AiCad", "sessions"),
            TimeSpan.FromSeconds(15),
            DefaultMaxMessageBytes);
    }
}

public sealed class CadBridgeException : Exception
{
    public CadBridgeException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

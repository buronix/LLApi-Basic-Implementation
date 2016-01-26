public class LLApiConnection{

    public int ConnectionId { get; private set; }
    public string Address { get; private set; }
    public int Port { get; private set; }

    public LLApiConnection(int connectionId, string address, int port)
    {
        ConnectionId = connectionId;
        Address = address;
        Port = port;
    }
}

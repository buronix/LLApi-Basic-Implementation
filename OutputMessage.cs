using DarkRift;
using UnityEngine.Networking;

public class OutputMessage {

    public NetworkWriter Data { private set; get; }
    public int ConnectionID { private set; get; }
    public int ChannelID { private set; get; }

    public enum MessageType : byte
    {
        Reply = 0,
        All = 1
    }

    public MessageType Type { get; private set; }

    public OutputMessage(NetworkWriter data)
    {
        Data = data;
        Type = MessageType.All;
        ChannelID = 0;
    }

    public OutputMessage(NetworkWriter data, int connectionID)
    {
        Data = data;
        ConnectionID = connectionID;
        Type = MessageType.Reply;
        ChannelID = 0;
    }

    public OutputMessage(NetworkWriter data, int connectionID, int channelID)
    {
        Data = data;
        ConnectionID = connectionID;
        Type = MessageType.Reply;
        ChannelID = channelID;
    }
}
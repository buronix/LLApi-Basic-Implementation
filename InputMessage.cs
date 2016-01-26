using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System;

public class InputMessage{

    public int ConnectionId { get; private set; }
    public int HostId { get; private set; }
    public int ChannelId { get; private set; }
    public NetworkReader Reader { get; private set; }
    public float ReceivedTime { get; private set; }
    public Subjects MsgSubject { get; private set; }
    public InputMessage(int connectionId, int hostId, int channelId, Subjects msgSubject, NetworkReader reader, float receivedTime)
    {
        ConnectionId = connectionId;
        HostId = hostId;
        ChannelId = channelId;
        MsgSubject = msgSubject;
        Reader = reader;
        ReceivedTime = receivedTime;
    }

}

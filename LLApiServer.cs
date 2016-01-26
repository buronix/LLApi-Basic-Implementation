using UnityEngine.Networking;
using UnityEngine;
using System.Threading;
using System;
using System.Collections.Generic;
using UnityEngine.Networking.Types;

public class LLApiServer : IDisposable {

    public int Port { get; private set; }
    public ushort MaxConnections { get; private set; }
    public ConnectionConfig Config { get; private set; }
    public HostTopology Topology { get; private set; }
    public int SocketId { get; private set; }
    public bool isConnected { get; private set; }
    public GameServer GameServer { get; private set; }
    public ushort MaxMessages { get; private set; }
    private Queue<InputMessage> InputMessagesQueue = new Queue<InputMessage>();
    public delegate void SubjectDelegate(InputMessage message);
    private Dictionary<Subjects, SubjectDelegate> SubjectHandlers = new Dictionary<Subjects, SubjectDelegate>();
    private object lockHandlers = new object();
    private Dictionary<int, LLApiConnection> Connections = new Dictionary<int, LLApiConnection>();

    private object lockConnectionObject = new object();

    private object lockPocessObject = new object();
    private Thread[] ProcessWorkers;
    private ushort workerProcessCount = 1;
    private bool Disconnect = false;
    private bool _stop = false;

    private Queue<OutputMessage> OutputMessagesQueue = new Queue<OutputMessage>();
    private object lockOutputObject = new object();

    public LLApiServer(GameServer gameServer, int port, ushort maxConnections, ushort maxMessages)
    {
        GameServer = gameServer;
        Port = port;
        MaxConnections = maxConnections;
        MaxMessages = maxMessages;
        isConnected = false;
        Config = new ConnectionConfig();
        int ReliableChannelId = Config.AddChannel(QosType.Reliable);
        int NonReliableChannelId = Config.AddChannel(QosType.Unreliable);
        Topology = new HostTopology(Config, MaxConnections);
    }

    public void RegisterHandler(Subjects subject, SubjectDelegate handler){
        lock (lockHandlers)
        {
            SubjectDelegate handlerDelegate;
            
            if (SubjectHandlers.TryGetValue(subject, out handlerDelegate))
            {
                handlerDelegate += handler;
            }
            else
            {
                SubjectHandlers.Add(subject, handler);
            }
        }
    }

    public bool RemoveHandler(Subjects subject, SubjectDelegate handler)
    {
        lock (lockHandlers)
        {
            SubjectDelegate handlerDelegate;

            if (!SubjectHandlers.TryGetValue(subject, out handlerDelegate))
            {
                return false;
            }
            handlerDelegate -= handler;
            return true;
        }
    }

    public bool SendMessage(int connectionId, NetworkWriter writer, out NetworkError Error)
    {
        return SendMessage(connectionId, writer, 0, out Error);
    }

    public bool SendMessage(int connectionId, NetworkWriter writer, int channel, out NetworkError Error)
    {
        byte error;
        byte[] buffer = writer.ToArray();
        NetworkTransport.Send(SocketId, connectionId, channel, buffer, buffer.Length, out error);
        Error = (NetworkError)error;
        if (Error != NetworkError.Ok)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public void Start()
    {
        Disconnect = false;
        GlobalConfig gConfig = new GlobalConfig();
        NetworkTransport.Init(gConfig);
        SocketId = NetworkTransport.AddHost(Topology, Port);
        ProcessWorkers = new Thread[workerProcessCount];

        // Create and start a separate thread for each worker
        for (int i = 0; i < workerProcessCount; i++)
        {
            ProcessWorkers[i] = new Thread(MessagesConsumer);
            ProcessWorkers[i].Name = "LLApi Process Thread " + i;
            ProcessWorkers[i].Start();

        }
    }


    private void addConnection(LLApiConnection connection)
    {
        lock (lockConnectionObject)
        {
            Connections.Add(connection.ConnectionId, connection);
        }
    }

    public bool TryGetConnection(int connectionId, out LLApiConnection connection)
    {
        connection = null;
        lock (lockConnectionObject)
        {
            return Connections.TryGetValue(connectionId, out connection);
        }
    }

    private bool removeConnection(int connectionId)
    {
        lock (lockConnectionObject)
        {
            return Connections.Remove(connectionId);
        }
    }

    private void addInputMessageToQueue(InputMessage message)
    {
        lock (lockPocessObject)
        {
            if (message != null)
            {
                Debug.Log("Message Added to Queue : " + message.MsgSubject);
            }
            InputMessagesQueue.Enqueue(message);
            Monitor.PulseAll(lockPocessObject);
        }
    }

    public void addOutPutMessageToQueue(OutputMessage message)
    {
        lock (lockOutputObject)
        {
            OutputMessagesQueue.Enqueue(message);
        }
    }

    private void MessagesConsumer()
    {
        try
        {
            while (true)
            {
                InputMessage message;
                lock (lockPocessObject)
                {
                    while (InputMessagesQueue.Count == 0)
                    {
                        Monitor.Wait(lockPocessObject);
                    }
                    message = InputMessagesQueue.Dequeue();
                }
                if (message == null) return;
                lock (lockHandlers)
                {
                    SubjectDelegate handlerDelegate;
                    if(SubjectHandlers.TryGetValue(message.MsgSubject, out handlerDelegate))
                    {
                        handlerDelegate.Invoke(message);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }

    }
    
    public void SendOutputMessages()
    {
        bool stopSending = false;
        ushort messagesSended = 0;
        while (!Disconnect && !stopSending)
        {
            OutputMessage message;
            lock (lockOutputObject)
            {
                if (OutputMessagesQueue.Count == 0)
                {
                    stopSending = true;
                    continue;
                }
                message = OutputMessagesQueue.Dequeue();
            }
            if (message == null) continue;
            messagesSended += ManageOutputMessage(message);
            if(messagesSended>= MaxMessages)
            {
                stopSending = true;
            }
        }
    }

    private ushort ManageOutputMessage(OutputMessage message)
    {
        NetworkError error;
        ushort messagesSended = 0;
        switch (message.Type)
        {
            case OutputMessage.MessageType.Reply:
                SendMessage(message.ConnectionID, message.Data, message.ChannelID, out error);
                messagesSended = 1;
                break;
            case OutputMessage.MessageType.All:
                List<int> connectionIDS = GameServer.UserManager.getConnectedUsersConnectionsID();
                foreach (int connectionID in connectionIDS)
                {
                    SendMessage(connectionID, message.Data, message.ChannelID, out error);
                }
                messagesSended = (ushort)connectionIDS.Count;
                break;
        }
        return messagesSended;
    }

    public void Listen()
    {
        bool stopListen = false;
        ushort messagesReaded = 0;
        while (!Disconnect && !stopListen)
        {
            messagesReaded++;
            int recConnectionId;
            int recChannelId;
            byte[] recBuffer = new byte[1024];
            int bufferSize = 1024;
            int dataSize;
            byte error;
            NetworkReader reader;
            InputMessage message;
            NetworkEventType recNetworkEvent;
            try {
                recNetworkEvent = NetworkTransport.ReceiveFromHost(SocketId, out recConnectionId, out recChannelId, recBuffer, bufferSize, out dataSize, out error);
                if (error != (byte)NetworkError.Ok)
                {
                    Debug.Log("Error [" + (NetworkError)error + "] receiving a Message from : " + SocketId + " | conID : " + recConnectionId + " | channel : " + recChannelId);
                }
                else
                {
                    switch (recNetworkEvent)
                    {
                        case NetworkEventType.Nothing:
                            stopListen = true;
                            break;
                        case NetworkEventType.ConnectEvent:
                            string address;
                            int remotePort;
                            NetworkID networkID;
                            NodeID nodeID;
                            NetworkTransport.GetConnectionInfo(SocketId, recConnectionId, out address, out remotePort, out networkID, out nodeID, out error);
                            if (error == (byte)NetworkError.Ok)
                            {
                                LLApiConnection connection = new LLApiConnection(recConnectionId, address, remotePort);
                                addConnection(connection);
                            }
                            reader = new NetworkReader(recBuffer);
                            message = new InputMessage(recConnectionId, SocketId, recChannelId, Subjects.Connect, reader, GameServer.getServerTime());
                            addInputMessageToQueue(message);
                            Debug.Log("New Connection: " + SocketId + "[" + address + ":" + remotePort + "] con: " + recConnectionId + " Size: " + dataSize);
                            break;
                        case NetworkEventType.DataEvent:
                            Debug.Log("Incoming message Dataevent received: " + SocketId + " con: " + recConnectionId + " Size: " + dataSize);
                            reader = new NetworkReader(recBuffer);
                            Subjects subject = (Subjects)reader.ReadUInt16();
                            message = new InputMessage(recConnectionId, SocketId, recChannelId, subject, reader, GameServer.getServerTime());
                            addInputMessageToQueue(message);
                            break;
                        case NetworkEventType.DisconnectEvent:
                            Debug.Log("Remote client event disconnected: " + SocketId + " con: " + recConnectionId + " Size: " + dataSize);
                            reader = new NetworkReader(recBuffer);
                            message = new InputMessage(recConnectionId, SocketId, recChannelId, Subjects.Disconnect, reader, GameServer.getServerTime());
                            addInputMessageToQueue(message);
                            removeConnection(recConnectionId);
                            break;
                    }
                }
            }catch(Exception e)
            {
                Debug.LogError("LLApiServer Error Receiving Data : " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                if (messagesReaded >= MaxMessages)
                {
                    stopListen = true;
                }
            }
        }
    }

    public void Stop()
    {
        _stop = true;
        Disconnect = true;
        if (isConnected)
        {
            NetworkTransport.RemoveHost(SocketId);
            Debug.Log("LLApiServer Shutting Down");
        }
        // Enqueue one null task per worker to make each exit.
        for (int i = 0; i < ProcessWorkers.Length; i++)
        {
            addInputMessageToQueue(null);
        }
        foreach (Thread worker in ProcessWorkers) worker.Join();
    }

    public void Dispose()
    {
        if (!_stop)
        {
            Stop();
        }
    }
}

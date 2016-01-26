using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Threading;

public class LLApiClient : IDisposable {

    public int SocketId { get; private set; }
    public string EndPoint { get; private set; }
    public ConnectionConfig Config { get; private set; }
    public HostTopology Topology { get; private set; }
    public GameClient GameClient { get; private set; }
    public int ConnectionId { get; private set; }
    public bool isConnected { get; private set; }
    public bool isConnecting { get; private set; }
    public int Port { get; private set; }
    private bool Disconnect = false;
    private bool _stop = false;
    private ushort MaxMessages = 50;
    private Queue<InputMessage> InputMessagesQueue = new Queue<InputMessage>();
    public delegate void SubjectDelegate(InputMessage message);
    private Dictionary<Subjects, SubjectDelegate> SubjectHandlers = new Dictionary<Subjects, SubjectDelegate>();
    private object lockHandlers = new object();
    private object lockPocessObject = new object();
    private Thread[] ProcessWorkers;
    private ushort workerProcessCount = 1;
    private Queue<OutputMessage> OutputMessagesQueue = new Queue<OutputMessage>();
    private object lockOutputObject = new object();

    public LLApiClient (string endPoint, int port, GameClient gameClient)
    {
        Config = new ConnectionConfig();
        int ReliableChannelId = Config.AddChannel(QosType.Reliable);
        int NonReliableChannelId = Config.AddChannel(QosType.Unreliable);
        Topology = new HostTopology(Config, 10);
        EndPoint = endPoint;
        Port = port;
        GameClient = gameClient;
        isConnected = false;
    }

    public bool Connect(out NetworkError error)
    {
        Disconnect = false;
        ProcessWorkers = new Thread[workerProcessCount];

        // Create and start a separate thread for each worker
        for (int i = 0; i < workerProcessCount; i++)
        {
            ProcessWorkers[i] = new Thread(MessagesConsumer);
            ProcessWorkers[i].Name = "LLApi Process Thread " + i;
            ProcessWorkers[i].Start();

        }
        SocketId = NetworkTransport.AddHost(Topology);
        byte errorByte;
        ConnectionId = NetworkTransport.Connect(SocketId, EndPoint, Port, 0, out errorByte);
        error = (NetworkError)errorByte;
        if (error != NetworkError.Ok)
        {
            isConnecting = false;
            isConnected = false;
            Debug.LogError("LLApiClient Error Connecting to " + EndPoint + ":" + Port + " Error: " + error);
        }
        else
        {
            isConnecting = true;
            Debug.Log("LLApiClient Connecting to " + EndPoint + ":" + Port + " ConnectionId : " + ConnectionId + "SocketID : "+SocketId);
        }
        return isConnecting;
    }

    public void Listen()
    {
        bool stopListen = false;
        ushort messagesReaded = 0;
        while ((isConnected || isConnecting) && !Disconnect && !stopListen)
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
            try
            {
                recNetworkEvent = NetworkTransport.ReceiveFromHost(SocketId, out recConnectionId, out recChannelId, recBuffer, bufferSize, out dataSize, out error);
                if (error != (byte)NetworkError.Ok)
                {
                    Debug.Log("Error [" + (NetworkError)error + "] receiving a Message conID : " + recConnectionId + " | channel : " + recChannelId);
                }
                else
                {
                    switch (recNetworkEvent)
                    {
                        case NetworkEventType.Nothing:
                            stopListen = true;
                            break;
                        case NetworkEventType.ConnectEvent:
                            Debug.Log("LLApiClient Succesfully Connected to " + EndPoint + ":" + Port + " ConnectionId : " + ConnectionId + " SocketId: "+ SocketId);
                            isConnected = true;
                            break;
                        case NetworkEventType.DataEvent:
                            Debug.Log("incoming message event received con: " + recConnectionId + " Size: " + dataSize);
                            reader = new NetworkReader(recBuffer);
                            Subjects subject = (Subjects)reader.ReadUInt16();
                            message = new InputMessage(recConnectionId, SocketId, recChannelId, subject, reader, GameClient.getClientTime());
                            addMessageToQueue(message);
                            break;
                        case NetworkEventType.DisconnectEvent:
                            Debug.Log("LLApiClient DisConnection");
                            isConnected = false;
                            break;
                    }
                }
            }
            catch (Exception e)
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

    public void RegisterHandler(Subjects subject, SubjectDelegate handler)
    {
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
    private void addMessageToQueue(InputMessage message)
    {
        lock (lockPocessObject)
        {
            InputMessagesQueue.Enqueue(message);
            Monitor.PulseAll(lockPocessObject);
        }
    }
    public void AddOutPutMessageToQueue(OutputMessage message)
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
                    if (SubjectHandlers.TryGetValue(message.MsgSubject, out handlerDelegate))
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
            NetworkError error;
            messagesSended++;
            SendMessage(message.Data, message.ChannelID, out error);
            if (error != NetworkError.Ok)
            {
                Debug.Log("Error Sending Message to " + message.ConnectionID);
            }
            if (messagesSended >= MaxMessages)
            {
                stopSending = true;
            }
        }
    }
    private bool SendMessage(NetworkWriter writer, out NetworkError error)
    {
        return SendMessage(writer, 0, out error);
    }

    private bool SendMessage(NetworkWriter writer, int channelId, out NetworkError error)
    {
        error = NetworkError.WrongConnection;
        if (!isConnected)
        {
            Debug.LogWarning("LLApiClient is not Connected");
            return false;
        }
        byte errorByte;
        byte[] buffer = writer.ToArray();
        NetworkTransport.Send(SocketId, ConnectionId, channelId, buffer, buffer.Length, out errorByte);
        error = (NetworkError)errorByte;
        if(error!= NetworkError.Ok)
        {
            Debug.LogError("LLApiClient Sock: " + SocketId + " ConId: " + ConnectionId + " Error Sending Message : " + error);
            return false;
        }
        return true;
    }

    public void Stop()
    {
        _stop = true;
        Disconnect = true;
        if (isConnected)
        {
            NetworkTransport.RemoveHost(SocketId);
            Debug.Log("LLApiClient Shutting Down");
        }
        // Enqueue one null task per worker to make each exit.
        for (int i = 0; i < ProcessWorkers.Length; i++)
        {
            addMessageToQueue(null);
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

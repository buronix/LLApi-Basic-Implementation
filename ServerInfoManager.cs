using System.Collections.Generic;
using System.Threading;
using System;

using UnityEngine;
using UnityEngine.Networking;

public class ServerInfoManager : IDisposable {

    public GameServer GameServer { get; private set; }
    private Queue<InputMessage> InputMessagesQueue = new Queue<InputMessage>();
    private object lockObject = new object();
    private Thread[] ManageWorkers;

    public ServerInfoManager(GameServer gameServer)
    {
        GameServer = gameServer;
    }

    //Here is the Action
    private void HandleMessage(InputMessage message)
    {
        switch (message.MsgSubject)
        {
            case Subjects.LoginRequest       : LoginRequest(message); break;
            case Subjects.LogOutRequest      : DisconnectRequest(message); break;
            case Subjects.ServerInfoRequest  : ServerInfoRequest(message); break;
        }
    }

    /// <summary>
    /// Send an Update to all Clients of the Status Connection of a Player
    /// </summary>
    /// <remarks>
    /// Order:
    /// String UserName
    /// bool ConnectedStatus
    /// float ServerTime
    /// </remarks>
    /// <param name="UserName"></param>
    /// <param name="connectedStatus"></param>
    /// <param name="ServerTime"></param>
    public void PlayerStatusUpdate(String UserName, bool connectedStatus, float ServerTime)
    {
        NetworkWriter data = new NetworkWriter();
        data.Write((ushort)Subjects.PlayerStatusUpdate);
        data.Write(UserName);
        data.Write(connectedStatus);
        data.Write(ServerTime);
        OutputMessage output = new OutputMessage(data);
        GameServer.LLApiServer.addOutPutMessageToQueue(output);
    }

    /// <summary>
    /// Request for Server Information
    /// </summary>
    /// <remarks>
    /// Order:
    /// String TokenID
    /// </remarks>
    /// <param name="message"></param>
    private void ServerInfoRequest(InputMessage message)
    {
        string tokenID = message.Reader.ReadString();
        ClientInfo client;
        string detailMessage;
        bool success = GameServer.UserManager.GetUserByTokenIDSecure(tokenID, message.ConnectionId, out client, out detailMessage);
        ServerInfoResponse(message.ConnectionId, success, detailMessage);
    }

    /// <summary>
    /// Return Information of the Server
    /// </summary>
    /// <remarks>
    /// Order:
    /// bool success
    /// float ServerTime
    /// string detailMessage
    /// ushort ConnetedUserCount -> UserNames Length
    /// string[] UserNames
    /// </remarks>
    /// <param name="connectionID"></param>
    /// <param name="success"></param>
    /// <param name="detailMessage"></param>
    private void ServerInfoResponse(int connectionID, bool success, string detailMessage)
    {
        NetworkWriter data = new NetworkWriter();
        data.Write((ushort)Subjects.ServerInfoResponse);
        data.Write(success);
        if (success)
        {
            data.Write(GameServer.getServerTime());
        }
        else
        {
            data.Write((float)0);
        }
        
        data.Write(detailMessage);
        if (!success)
        {
            data.Write((ushort)0);
        }
        else
        {
            List<string> UserNames = GameServer.UserManager.getConnectedUserNames();
            data.Write((ushort)UserNames.Count);
            foreach(string userName in UserNames)
            {
                data.Write(userName);
            }
        }
        OutputMessage output = new OutputMessage(data, connectionID);
        GameServer.LLApiServer.addOutPutMessageToQueue(output);
    }

    /// <summary>
    /// Login the User in The System
    /// </summary>
    /// <remarks>
    ///  Order:
    ///  String UserID
    /// </remarks>
    /// <param name="message"> Message To Handle </param>
    private void LoginRequest(InputMessage message)
    {
        string userID = message.Reader.ReadString();
        string tokenID;
        string loginMessage;
        bool success = GameServer.UserManager.ConnectUser(userID, message.ConnectionId, message.HostId,
            message.ReceivedTime, out tokenID, out loginMessage);
        LoginResponse(message.ConnectionId, success, tokenID, loginMessage);

        Debug.Log(loginMessage);
    }

    /// <summary>
    /// Sends a response to the Login Attempt
    /// </summary>
    /// <remarks>
    /// Order:
    /// bool Success
    /// string token
    /// string detailMessage
    /// float serverTime
    /// </remarks>
    private void LoginResponse(int connectionID, bool success, string token, string detailMessage)
    {
        NetworkWriter data = new NetworkWriter();
        data.Write((ushort)Subjects.LoginResponse);
        data.Write(success);
        data.Write(token);
        data.Write(detailMessage);
        data.Write(GameServer.getServerTime());
        OutputMessage output = new OutputMessage(data, connectionID);
        GameServer.LLApiServer.addOutPutMessageToQueue(output);
    }

    /// <summary>
    /// Disconnect the User from The System
    /// </summary>
    /// <remarks>
    ///  Order:
    ///  String TokenID
    /// </remarks>
    /// <param name="message"> Message To Handle </param>
    private void DisconnectRequest(InputMessage message)
    {
        string tokenID = message.Reader.ReadString();
        string loginMessage;
        bool success = GameServer.UserManager.DisconnectUserByTokenID(tokenID);
        if (success)
        {
            loginMessage = "User Disconnected Successfully";
        }
        else
        {
            loginMessage = "Error in User Disconnection";
        }
        DisconnectResponse(message.ConnectionId, success, loginMessage);
        Debug.Log(loginMessage);
    }
    /// <summary>
    ///  Return the DisconnectResponse to the client
    /// </summary>
    /// <remarks>
    ///  Order:
    ///  String success
    ///  String detailMessage
    ///  float serverTime
    /// </remarks>
    /// <param name="connectionID"></param>
    /// <param name="success"></param>
    /// <param name="detailMessage"></param>
    private void DisconnectResponse(int connectionID, bool success, string detailMessage)
    {
        NetworkWriter data = new NetworkWriter();
        data.Write((ushort)Subjects.LogOutResponse);
        data.Write(success);
        data.Write(detailMessage);
        data.Write(GameServer.getServerTime());
        OutputMessage output = new OutputMessage(data, connectionID);
        GameServer.LLApiServer.addOutPutMessageToQueue(output);
    }

    public void Start(int workerCount)
    {
        ManageWorkers = new Thread[workerCount];

        // Create and start a separate thread for each worker
        for (int i = 0; i < workerCount; i++)
        {
            ManageWorkers[i] = new Thread(MessagesConsumer);
            ManageWorkers[i].Name = "ServerInfo Manager Consumer " + i;
            ManageWorkers[i].Start();

        }
    }

    public void addMessageToQueue(InputMessage message)
    {
        lock (lockObject)
        {
            InputMessagesQueue.Enqueue(message);
            Monitor.PulseAll(lockObject);
        }
    }

    private void MessagesConsumer()
    {
        try
        {
            while (true)
            {
                InputMessage message;
                lock (lockObject)
                {
                    while (InputMessagesQueue.Count == 0)
                    {
                        Monitor.Wait(lockObject);
                    }
                    message = InputMessagesQueue.Dequeue();
                }
                if (message == null) return;         // This signals our exit
                HandleMessage(message);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }

    }
    public void Dispose()
    {
        // Enqueue one null task per worker to make each exit.
        for (int i = 0; i < ManageWorkers.Length; i++)
        {
            addMessageToQueue(null);
        }
        foreach (Thread worker in ManageWorkers) worker.Join();
    }
}

using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System;

public class GameClient : MonoBehaviour {

    public string IP = "127.0.0.1";
    public int Port = 8888;
    public string UserID = "ExampleUser1";
    private bool LoginStatus = false;
    private bool TryLogin = false;
    public List<string> ConnectedUsers { get; private set; }
    public LLApiClient LLApiClient;
    private string TokenID;
    private float ServerTime;
    private float _ClientTime;
    private object lockTimeObject = new object();

    void Awake()
    {
        ConnectedUsers = new List<String>();
        setClientTime(Time.time);
        LLApiClient = new LLApiClient(IP, Port, this);
        LLApiClient.RegisterHandler(Subjects.Connect, onClientConnection);
        LLApiClient.RegisterHandler(Subjects.Disconnect, onClientDisConnection);
        LLApiClient.RegisterHandler(Subjects.LoginResponse, onLoginResponse);
        LLApiClient.RegisterHandler(Subjects.LogOutResponse, onLogOutResponse);
        LLApiClient.RegisterHandler(Subjects.ServerInfoResponse, onServerInfoResponse);
        LLApiClient.RegisterHandler(Subjects.PlayerStatusUpdate, onPlayerStatusUpdate);
        LLApiClient.RegisterHandler(Subjects.ServerMessage, onServerMessage);
    }

    void Start()
    {
        setClientTime(Time.time);

    }

    void onClientConnection(InputMessage message)
    {

    }

    void onClientDisConnection(InputMessage message)
    {

    }

    void onLoginResponse(InputMessage message)
    {
        LoginStatus = message.Reader.ReadBoolean();
        TokenID = message.Reader.ReadString();
        string detailMessage = message.Reader.ReadString();
        ServerTime = message.Reader.ReadSingle();
        Debug.Log("Login : " + LoginStatus + " | Token : " + TokenID + " | Message : " + detailMessage + " | Server Time : " + ServerTime);
        if (LoginStatus)
        {
            NetworkWriter writer = new NetworkWriter();
            writer.Write((ushort)Subjects.ServerInfoRequest);
            writer.Write(TokenID);
            OutputMessage outMessage = new OutputMessage(writer);
            LLApiClient.AddOutPutMessageToQueue(outMessage);
        }
    }

    void onLogOutResponse(InputMessage message)
    {

    }

    void onPlayerStatusUpdate(InputMessage message)
    {
        string userName = message.Reader.ReadString();
        bool ConnectedStatus = message.Reader.ReadBoolean();
        ServerTime = message.Reader.ReadSingle();

        if (ConnectedStatus)
        {
            ConnectedUsers.Add(userName);
        }
        else
        {
            ConnectedUsers.Remove(userName);
        }
        Debug.Log("PlayerUpdate: " + userName + " Time: " + ServerTime + "Connected: " + ConnectedStatus);
    }

    void onServerInfoResponse(InputMessage message)
    {
        bool success = message.Reader.ReadBoolean();
        ServerTime = message.Reader.ReadSingle();
        string detailMessage = message.Reader.ReadString();
        ushort UserCount = 0;
        if (success)
        {
            UserCount = message.Reader.ReadUInt16();
            string[] UserNames = new string[UserCount];
            for (int i = 0; i < UserCount; i++)
            {
                UserNames[i] = message.Reader.ReadString();
            }
            ConnectedUsers.AddRange(UserNames);
        }
        Debug.Log("ServerInfo: " + success + " Time: " + ServerTime + " ConnectedUsers: " + UserCount);
    }

    void onServerMessage(InputMessage message)
    {

    }

    void Update()
    {
        setClientTime(Time.time);
        if (LLApiClient.isConnected || LLApiClient.isConnecting)
        {
            LLApiClient.Listen();
        }

        if (LLApiClient.isConnected == false && LLApiClient.isConnecting == false)
        {
            NetworkError error;
            LLApiClient.Connect(out error);
            if (error != NetworkError.Ok)
            {
                Debug.LogError("Error trying to Connect to Server: " + error);
            }
        }
        if(TryLogin == false && LLApiClient.isConnected)
        {
            TryLogin = true;
            NetworkWriter writer = new NetworkWriter();
            writer.Write((ushort)Subjects.LoginRequest);
            writer.Write(UserID);

            OutputMessage outMessage = new OutputMessage(writer);
            LLApiClient.AddOutPutMessageToQueue(outMessage);
        }

        if (LLApiClient.isConnected)
        {
            LLApiClient.SendOutputMessages();
        }
       
    }
    
    void OnApplicationQuit()
    {
        LLApiClient.Stop();
    }
    
    private void setClientTime(float time)
    {
        lock (lockTimeObject)
        {
            _ClientTime = time;
        }
    }

    public float getClientTime()
    {
        lock (lockTimeObject)
        {
            return _ClientTime;
        }
    }
}

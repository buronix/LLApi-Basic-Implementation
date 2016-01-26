using System.Collections.Generic;
using System;

using UnityEngine;
using System.Linq;

public class UserManager {

    private Dictionary<string, ClientInfo> Clients = new Dictionary<string, ClientInfo>();

    private Dictionary<string, ClientInfo> TokenClients = new Dictionary<string, ClientInfo>();

    private Dictionary<int, ClientInfo> ConnectedClients = new Dictionary<int, ClientInfo>();

    public GameServer GameServer { get; private set; }

    private object lockObject = new object();

    private object lockConnectedObject = new object();

    private object lockTokenObject = new object();

    private List<int> _clientsConnectedCached;
    private List<string> _clientsConnectedUserNameCached;

    private bool _clientsConnectedStatusUpdate = true;
    private bool _clientsConnectedUserNameStatusUpdate = true;

    public UserManager(GameServer gameServer)
    {
        GameServer = gameServer;
    }

    public bool GetUserByTokenIDSecure(string tokenID, int connectionID, out ClientInfo client, out string detailMessage)
    {
        lock (lockTokenObject)
        {
            if (!TokenClients.TryGetValue(tokenID, out client))
            {
                detailMessage = "There is no User with TokenID: " + tokenID;
                Debug.LogWarning(detailMessage);
                return false;
            }
        }

        ClientInfo clientSecure;
        lock (lockConnectedObject)
        {
            if (!ConnectedClients.TryGetValue(connectionID, out clientSecure))
            {
                detailMessage = "No Client Connected from: " + client.ConnectionID;
                Debug.LogWarning(detailMessage);
                return false;
            }
        }
        if (!clientSecure.UserID.Equals(client.UserID))
        {
            detailMessage = "Client Connected from: " + client.ConnectionID+" is different from TokenID: " + tokenID;
            Debug.LogWarning(detailMessage);
            return false;
        }
        detailMessage = "Success";
        return true;
    }

    public bool ConnectUser(string userID, int connectionID, int senderID, float sessionTime, out string tokenID, out string message)
    {
        ClientInfo client;
        lock (lockObject)
        {
            if(!Clients.TryGetValue(userID, out client)){
                message = "Error :: User " + userID + " does not exist";
                tokenID = "NoToken";
                Debug.LogWarning(message);
                return false;
            }
        }
        lock (lockConnectedObject)
        {
            if (ConnectedClients.ContainsKey(connectionID))
            {
                message = "Error :: User " + userID + " is Connected with " + connectionID;
                tokenID = "NoToken";
                Debug.LogError(message);
                return false;
            }
            client.Connect(connectionID, senderID, sessionTime, DateTime.Now);
            ConnectedClients.Add(connectionID, client);
            _clientsConnectedStatusUpdate = true;
            _clientsConnectedUserNameStatusUpdate = true;
        }
        string genToken = generateToken(userID);
        lock (lockTokenObject)
        {
            if ((client.TokenID != null && client.TokenID.Length >= 0 ) || TokenClients.ContainsKey(genToken))
            {
                message = "Error :: User " + userID + " has a Token Generated or the Token exists : " + genToken;
                tokenID = "NoToken";
                return false;
            }
            client.setTokenID(genToken);
            TokenClients.Add(genToken, client);
            message = "Success :: User " + userID + " is Connected";
            tokenID = genToken;
        }
        GameServer.ServerInfoManager.PlayerStatusUpdate(client.UserName, client.isConnected, GameServer.getServerTime());
        return true;
    }

    public bool DisconnectUserByID(string userID)
    {
        ClientInfo client;
        lock (lockObject)
        {
            if (!Clients.TryGetValue(userID, out client))
            {
                Debug.LogWarning("There is no User : " + userID);
                return false; 
            }
        }
        return DisconnectUserByTokenID(client.TokenID);
    }

    public bool DisconnectUserByTokenID(string tokenID)
    {
        if(tokenID == null || tokenID.Length <= 0)
        {
            return false;
        }

        ClientInfo client;

        lock (lockTokenObject)
        {
            if(!TokenClients.TryGetValue(tokenID, out client))
            {
                Debug.LogWarning("There is no User with TokenID: " + tokenID);
                return false;
            }
            TokenClients.Remove(client.TokenID);
        }

        lock (lockConnectedObject)
        {
            if (!ConnectedClients.Remove(client.ConnectionID))
            {
                Debug.LogWarning("No Client Connected from: " + client.ConnectionID);
                return false;
            }
            _clientsConnectedStatusUpdate = true;
            _clientsConnectedUserNameStatusUpdate = true;
        }
        client.Disconnect(DateTime.Now);
        GameServer.ServerInfoManager.PlayerStatusUpdate(client.UserName, client.isConnected, GameServer.getServerTime());
        return true;
    }

    public bool DisconnectByConnection(int connectionID)
    {
        ClientInfo client;
        lock (lockConnectedObject)
        {
            if (!ConnectedClients.TryGetValue(connectionID, out client))
            {
                Debug.LogWarning("No Client Connected from: "+connectionID);
                return false;
            }
            ConnectedClients.Remove(client.ConnectionID);
            _clientsConnectedStatusUpdate = true;
            _clientsConnectedUserNameStatusUpdate = true;
        }
        if (client.TokenID == null)
        {
            Debug.LogWarning("Client " + client.UserID + " ConID [" + connectionID + "] Has no tokenID");
            return false;
        }
        lock (lockTokenObject)
        {
            TokenClients.Remove(client.TokenID);
        }
        client.Disconnect(DateTime.Now);
        GameServer.ServerInfoManager.PlayerStatusUpdate(client.UserName, client.isConnected, GameServer.getServerTime());
        return true;
    }

    public bool createUser(string userID, string userName)
    {
        lock (lockObject)
        {
            if (Clients.ContainsKey(userID))
            {
                Debug.Log("UserManager: Cannot create a User :: Duplicate ID: " + userID);
                return false;
            }
            else
            {
                ClientInfo client = new ClientInfo(userID, userName, this);
                Clients.Add(userID, client);
                return true;
            }
        }
    }

    public bool tryGetUserByID(string userID, out ClientInfo client)
    {
        lock (lockObject)
        {
            return Clients.TryGetValue(userID, out client);
        }
    }

    public List<int> getConnectedUsersConnectionsID()
    {
        lock (lockConnectedObject)
        {
            if (_clientsConnectedStatusUpdate)
            {
                _clientsConnectedCached = new List<int>();
                _clientsConnectedCached.AddRange(ConnectedClients.Keys);
                _clientsConnectedStatusUpdate = false;
            }
           
            return _clientsConnectedCached;
        }
    }

    public List<string> getConnectedUserNames()
    {
        lock (lockConnectedObject)
        {
            if (_clientsConnectedUserNameStatusUpdate)
            {
                _clientsConnectedUserNameCached = new List<string>();
                foreach (ClientInfo client in ConnectedClients.Values)
                {
                    _clientsConnectedUserNameCached.Add(client.UserName);
                }
                _clientsConnectedUserNameStatusUpdate = false;
            }

            return _clientsConnectedUserNameCached;
        }
    }

    private string generateToken(string userID) {
        byte[] time = BitConverter.GetBytes(DateTime.UtcNow.ToBinary());
        byte[] key = Guid.NewGuid().ToByteArray();
        byte[] id = BitConverter.GetBytes(userID.GetHashCode());
        return Convert.ToBase64String(key.Concat(time).Concat(id).ToArray());
    }
}

using System;

public class ClientInfo {

    public string UserID { get; private set; }
    public string UserName { get; private set; }
    public bool isConnected { get; private set; }
    public DateTime LastConnectionTime { get; private set; }
    public float SessionTime { get; private set; }
    public UserManager UserManager { get; private set; }
    public int ConnectionID { get; private set; }
    public int SenderID { get; private set; }
    public string TokenID { get; private set; }

    public ClientInfo (string userID, string userName, UserManager userManager)
    {
        UserID = userID;
        UserName = userName;
        UserManager = userManager;
    }

    public void Connect(int connectionID, int senderID, float sessionTime, DateTime connectionTime)
    {
        ConnectionID = connectionID;
        SenderID = senderID;
        SessionTime = sessionTime;
        LastConnectionTime = connectionTime;

        isConnected = true;
    }

    public void setTokenID(string tokenID)
    {
        TokenID = tokenID;
    }

    public void Disconnect(DateTime connectionTime)
    {
        isConnected = false;
        LastConnectionTime = connectionTime;
        TokenID = null;
        SessionTime = 0;
    }
}

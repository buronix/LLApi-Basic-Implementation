using UnityEngine;

public class GameServer : MonoBehaviour
{

    public ServerInfoManager ServerInfoManager { get; private set; }
    public UserManager UserManager { get; private set; }
    public LLApiServer LLApiServer { get; private set; }
    public int Port = 8888;
    public string[] Users;
    private float _ServerTime;
    private object lockTimeObject = new object();

    void Awake()
    {
        setServerTime(Time.time);

        UserManager = new UserManager(this);
        for(int i =0; i < Users.Length; i++)
        {
            UserManager.createUser(Users[i], Users[i]+"_UserName");
        }
        ServerInfoManager = new ServerInfoManager(this);
        ServerInfoManager.Start(1);
        LLApiServer = new LLApiServer(this, Port, 200, 500);
        LLApiServer.RegisterHandler(Subjects.Connect, onServerConnection);
        LLApiServer.RegisterHandler(Subjects.LoginRequest, ServerInfoManager.addMessageToQueue);
        LLApiServer.RegisterHandler(Subjects.LogOutRequest, ServerInfoManager.addMessageToQueue);
        LLApiServer.RegisterHandler(Subjects.ServerInfoRequest, ServerInfoManager.addMessageToQueue);
        LLApiServer.Start();
    }

    public void onServerConnection(InputMessage message)
    {
        Debug.Log("GameServer : New Connection on Server : " + message.ConnectionId);
    }

    // Use this for initialization
    void Start()
    {
        setServerTime(Time.time);
        /**
        NetworkProximityChecker ProxChecker = new NetworkProximityChecker();
        ProxChecker.checkMethod = NetworkProximityChecker.CheckMethod.Physics3D;
        ProxChecker.visRange = 25;
        ProxChecker.visUpdateInterval = 1f;
        **/
    }

    // Update is called once per frame
    void Update()
    {
        setServerTime(Time.time);
        LLApiServer.Listen();
        LLApiServer.SendOutputMessages();
    }

    private void setServerTime(float time)
    {
        lock (lockTimeObject)
        {
            _ServerTime = time;
        }
    }

    public float getServerTime()
    {
        lock (lockTimeObject)
        {
            return _ServerTime;
        }
    }

    void OnApplicationQuit()
    {
        LLApiServer.Stop();
    }
}
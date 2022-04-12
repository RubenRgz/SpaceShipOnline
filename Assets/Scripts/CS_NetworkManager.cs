using System.Collections;
using System.Collections.Generic;
using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

/// <summary>
/// Socket object
/// </summary>
public class SocketObject
{
    public Socket WorkSocket = null;
    public int NetID = 0;
    public bool isActive = true;
    public byte[] ReceiveBuffer;
    public byte[] SendBuffer;
    public List<byte[]> Messages = new List<byte[]>();
    public int MessageBufferSize = 0;
    public int ConnectionStrikes = 0;
}

/// <summary>
/// Tokens the server and client will use in their communication
/// </summary>
public enum ENetToken
{
    SuccessfulConnection = 0,
    ClientLimitReached,
    RequestToServer,
    Synchronize,
    SpawnPlayer,
    ReadyToUpdate,
    Ping,
    Input,
    GameState,
    SpawnBubble,
    Collision, 
    Respawn,
    PlayerPosition
}

/// <summary>
/// Server request types
/// </summary>
public enum ERequestType
{
    Synchronize = 0,
    Spawn,
    StartGame,
    Respawn
}

/// <summary>
/// Client input types
/// </summary>
public enum EInputType
{
    Up = 0,
    Down,
    Left,
    Right,
    Shoot
}

/// <summary>
/// Kind of collisions
/// </summary>
public enum ECollisionType
{
    Ship = 0,
    Bullet
}

/// <summary>
/// Important player data to share
/// </summary>
struct SPlayerData
{
    public GameObject PlayerGameObj;
    public CS_ShipPlayer PlayerComponent;
    public Vector2 Position;
    public int GameID;
    public int Score;
    public int NumOfLives;
}

public class CS_NetworkManager : MonoBehaviour
{
    #region [Variables]
    // Basic singleton to have access to the Network Manager
    public static CS_NetworkManager Instance { get; private set; }

    //---------------------------------------------------------------------------------
    // Server settings
    //---------------------------------------------------------------------------------
    [Range(1, 4)]
    public int NumOfClients = 1;
    public int MessageBufferSize = 1024;
    string ConnectAddress = "192.168.100.37";
    // This is the local port use to start a connection with another machine (server)
    public int ConnectPort = 1025;
    // This is the port that will be listening for connections
    public int ServerListenPort = 1025;

    // Game
    public GameObject PlayerPrefab = null;
    public bool IsDebugMode = false;
    public bool IsConnectionDebug = false;

    //---------------------------------------------------------------------------------
    // Server members
    //---------------------------------------------------------------------------------
    Socket ListeningSocket = null;
    public bool IsServer { get; private set; } 
    int MaxQueueConnections = 100;
    bool ServerIsClosing = false;

    // Ping system variables
    bool IsPingActive = false;
    float DeltaTimePing = 0.0f;
    float TimeToSendPing = 5.0f; // seconds
    float TimeToCheckSocketsActivity = 30.0f; // seconds
    float DeltaSyncTime = 0.0f;
    float TimeToSynchronize = 15.0f; // seconds

    List<SocketObject> ServerSockets = new List<SocketObject>();
    List<SocketObject> ReadySockets = new List<SocketObject>();
    static int NetID = 1;

    // Game
    const int InitialNumOfLives = 3;
    const int InitialScore = 0;

    //---------------------------------------------------------------------------------
    // Client members
    //---------------------------------------------------------------------------------
    SocketObject ClientObject = null;
    bool IsConnected = false;
    public bool IsClient { get; private set; }

    //---------------------------------------------------------------------------------
    // Both (Server & Client) members
    //---------------------------------------------------------------------------------
    const int HeaderMessageSize = 8;
    const int SyncHeaderMessageSize = 12;

    //-----------------------------------------------------------------------------------------
    //-----------------------------------------------------------------------------------------
    // IMPORTANT!!! -> This table needs to be updated if the Game Token structure adds new data
    //-----------------------------------------------------------------------------------------
    //-----------------------------------------------------------------------------------------
    const int SuccessfulConnectionMessageSize = 4;
    const int ClientLimitReachedMessageSize = 0;
    const int RequestToServerMessageSize = 8;
    const int SynchronizMessageSize = 24;
    const int SpawnPlayerMessageSize = 24;
    const int ReadyToUpdateMessageSize = 4;
    const int PingMessageSize = 4;
    const int InputMessageSize = 8;
    const int GameStateMessageSize = 4;
    const int SpawnBubbleMessageSize = 4;
    const int BulletCollisionMessageSize = 16;
    const int ShipCollisionMessageSize = 16;
    const int ShipRespawnMessageSize = 12;
    const int PlayerPositionMessageSize = 12;


    // Players data
    Dictionary<int, SPlayerData> Players = new Dictionary<int, SPlayerData>();

    // Messags system variables
    CS_PackagePoolManager PoolPackage = null;

    //---------------------------------------------------------------------------------
    // Socket Layer
    //---------------------------------------------------------------------------------
    AsyncCallback OnAcceptCallBack;
    AsyncCallback OnClientSendsTCPCallBack;
    AsyncCallback OnClientReceivesTCPCallBack;
    AsyncCallback OnServerSendsTCPCallBack;
    AsyncCallback OnServerReceivesTCPCallBack;

    // Actions
    public Action OnClientConnectedEvent;
    public Action OnClientFailedToConnectEvent;
    public Action OnClientDisconnectedEvent;
    public Action OnServerStartedEvent;
    public Action OnServerClosedEvent;
    #endregion

    #region [Unity Functions]
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        OnAcceptCallBack = new AsyncCallback(OnAcceptConnection);
        OnClientSendsTCPCallBack = new AsyncCallback(OnClientSendsTCPData);
        OnClientReceivesTCPCallBack = new AsyncCallback(OnClientReceivesTCPData);
        OnServerSendsTCPCallBack = new AsyncCallback(OnServerSendsTCPData);
        OnServerReceivesTCPCallBack = new AsyncCallback(OnServerReceivesTCPData);

        OnClientConnectedEvent += OnClientConnectedHandler;
        OnClientFailedToConnectEvent += OnClientFailedToConnectHandler;
        OnClientDisconnectedEvent += OnClientDisconnectedHandler;
        OnServerStartedEvent += OnServerStartedHandler;
        OnServerClosedEvent += OnServerClosedHandler;

        // Add pool package component
        PoolPackage = gameObject.AddComponent<CS_PackagePoolManager>();
        if (PoolPackage != null)
            PoolPackage.Init(MessageBufferSize, 40);

        IsServer = false;
        IsClient = false;
    }

    // Update is called once per frame
    void Update()
    {
        if(IsServer)
        {
            CheckServerMessages();

            // Send feedback messages to all the ready clients
            foreach (var Socket in ServerSockets)
            {
                SocketObject SocketObj = Socket;
                ServerSendsTCPData(ref SocketObj);

                // Reset size counter
                SocketObj.MessageBufferSize = 0;
            }
        }
        else if(IsClient)
        {
            CheckClientMessages();

            if(IsConnected)
            {
                // Constant flow of information to send messages
                // to the the server if needed
                ClientSendsTCPData();

                // Reset size counter
                if(ClientObject != null)
                    ClientObject.MessageBufferSize = 0;
            }
        }
    }

    private void FixedUpdate()
    {
        if(IsServer)
        {
            if (ServerSockets.Count > 0)
            {
                DeltaSyncTime += Time.fixedDeltaTime;
                DeltaTimePing += Time.fixedDeltaTime;
                if (DeltaSyncTime >= TimeToSynchronize)
                {
                    // Sends sync data to all the clients to have the same info the server has
                    SendSyncrhornizeMulticast();
                    DeltaSyncTime = 0;
                }
                if (!IsPingActive && DeltaTimePing >= TimeToSendPing)
                {
                    // Sends ping to all the connected clients
                    StartPing();

                    // Reset time
                    DeltaTimePing = 0.0f;
                }
                else if (IsPingActive && DeltaTimePing >= TimeToCheckSocketsActivity)
                {
                    // Check clients connection with ping activity
                    CheckPingActivity();

                    // Reset time and flag
                    DeltaTimePing = 0.0f;
                    IsPingActive = false;
                }
            }
        }
    }

    private void OnDestroy()
    {
        OnClientConnectedEvent -= OnClientConnectedHandler;
        OnClientFailedToConnectEvent -= OnClientFailedToConnectHandler;
        OnClientDisconnectedEvent -= OnClientDisconnectedHandler;
        OnServerClosedEvent -= OnServerClosedHandler;

        if (IsServer)
            CloseTCPServer();
        else if (IsClient)
            CloseTCPSocket(ref ClientObject);
    }
    #endregion

    #region [Sockets Layer]
    /// <summary>
    /// Initializes the network connection as server or client
    /// </summary>
    /// <param name="_address"></param>Connect address
    /// <param name="_isServer"></param>True if we want to run server logic
    public void InitConnection(string _address, bool _isServer)
    {
        ConnectAddress = _address;

        if (_isServer)
            StartServer();
        else
            StartClient();
    }

    /// <summary>
    /// Start server socket services
    /// </summary>
    private void StartServer()
    {
        StartTCPServerSocket();
    }

    /// <summary>
    /// Start client socket services
    /// </summary>
    private void StartClient()
    {
        StartTCPClientSocket();
    }

    /// <summary>
    /// Server sends data to a client
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    private void ServerSendsTCPData(ref SocketObject _socketObj)
    {
        // Package all the messages to send
        if(PackageData(ref _socketObj))
        {
            // Send data
            try
            {
                _socketObj.WorkSocket.BeginSend(_socketObj.SendBuffer, 0, MessageBufferSize,
                    SocketFlags.None, OnServerSendsTCPCallBack, _socketObj);
            }
            catch (SocketException e)
            {
                Debug.LogError("WinSock Error: " + e.ToString());
            }
        }
    }

    /// <summary>
    /// Server start receiving data from the clients
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    private void ServerStartReceivingTCPData(ref SocketObject _socketObj)
    {
        try
        {
            _socketObj.WorkSocket.BeginReceive(_socketObj.ReceiveBuffer, 0, MessageBufferSize, 
                SocketFlags.None, OnServerReceivesTCPCallBack, _socketObj);
        }
        catch (SocketException e)
        {
            Debug.LogError("WinSock Error: " + e.ToString());
        }
    }

    /// <summary>
    /// Client sends data to the server
    /// </summary>
    private void ClientSendsTCPData()
    {
        // Package all the messages to send
        if(PackageData(ref ClientObject))
        {
            try
            {
                ClientObject.WorkSocket.BeginSend(ClientObject.SendBuffer, 0, MessageBufferSize,
                    SocketFlags.None, OnClientSendsTCPCallBack, ClientObject);
            }
            catch (SocketException e)
            {
                Debug.LogError("WinSock Error: " + e.ToString());
            }
        }
    }

    /// <summary>
    /// Client start receiving data from the server
    /// </summary>
    private void ClientRecievesTCPData()
    {
        try
        {
            ClientObject.WorkSocket.BeginReceive(ClientObject.ReceiveBuffer, 0, MessageBufferSize,
                SocketFlags.None, OnClientReceivesTCPCallBack, ClientObject);
        }
        catch (SocketException e)
        {
            Debug.LogError("WinSock Error: " + e.ToString());
        }
    }

    /// <summary>
    /// Closes server socket
    /// </summary>
    private void CloseTCPServer()
    {
        // Close Listen scoket
        if (ListeningSocket != null)
        {
            ListeningSocket.Close();
            ServerIsClosing = true;
        }

        // Clear player objects
        foreach (var item in Players)
        {
            Destroy(item.Value.PlayerGameObj);
        }
        Players.Clear();

        // Close all connected sockets
        ReadySockets.Clear();
        for (int i = 0; i < ServerSockets.Count; i++)
        {
            SocketObject SocketObj = ServerSockets[i];
            CloseTCPSocket(ref SocketObj);
        }
        ServerSockets.Clear();
    }

    /// <summary>
    /// Closes client sockets
    /// </summary>
    /// <param name="_socketObject"></param>Socket reference to work with
    private void CloseTCPSocket(ref SocketObject _socketObject)
    {
        // Clear arrays
        Array.Clear(_socketObject.SendBuffer, 0, _socketObject.SendBuffer.Length);
        Array.Clear(_socketObject.ReceiveBuffer, 0, _socketObject.ReceiveBuffer.Length);

        // Cleare messages
        _socketObject.Messages.Clear();

        if(IsClient)
        {
            // Clear player objects
            foreach (var item in Players)
            {
                Destroy(item.Value.PlayerGameObj);
            }
            Players.Clear();
        }

        // Close socket
        if (_socketObject.WorkSocket != null)
        {
            if (_socketObject.WorkSocket.Connected)
            {
                try
                {
                    _socketObject.WorkSocket.Shutdown(SocketShutdown.Send);
                }
                finally
                {
                    _socketObject.WorkSocket.Close();
                }
            }
            else
            {
                _socketObject.WorkSocket.Close();
            }
        }
        _socketObject.WorkSocket = null;

        // Clear Object
        _socketObject = null;
    }

    /// <summary>
    /// Start server services
    /// </summary>
    private void StartTCPServerSocket()
    {
        // Convert from IPv4 address string to 32-bit value
        UInt32 IPaddress = ConvertFromIpAddressToInteger(ConnectAddress);

        // Set IPv4 address and port
        IPEndPoint IPPoint = new IPEndPoint(IPaddress, ServerListenPort);

        // Crate socket with (IPv4 address family - Stream socket type - TCP protocol)
        ListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        if (ListeningSocket != null)
        {
            try
            {
                // Bind socket to the IP address and port
                ListeningSocket.Bind(IPPoint);

                // Server starts
                OnServerStartedEvent?.Invoke();
            }
            catch (SocketException e)
            {
                Debug.LogError("WinSock Error: " + e.ToString());

                return;
            }

            // Listen for clients
            ListeningSocket.Listen(MaxQueueConnections); // The maximum length of the pending connections queue.

            try
            {
                // Start accepting connections async
                ListeningSocket.BeginAccept(OnAcceptCallBack, ListeningSocket);
            }
            catch (SocketException e)
            {
                Debug.LogError("WinSock Error: " + e.ToString());
            }
        }
    }

    /// <summary>
    /// Start client connection
    /// </summary>
    private void StartTCPClientSocket()
    {
        // Convert from IPv4 address string to 32-bit value
        UInt32 IPaddress = ConvertFromIpAddressToInteger(ConnectAddress);

        // Set IPv4 address and port
        IPEndPoint IPPoint = new IPEndPoint(IPaddress, ConnectPort);

        // Create socket object
        ClientObject = new SocketObject();

        // Create socket with (IPv4 address family - Stream socket type - TCP protocol)
        ClientObject.WorkSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        ClientObject.SendBuffer = new byte [MessageBufferSize];
        ClientObject.ReceiveBuffer = new byte [MessageBufferSize];

        try
        {
            // Blocks main thread until it has connection or fails
            ClientObject.WorkSocket.Connect(IPPoint);

            if (!ClientObject.WorkSocket.Connected)
            {
                Debug.LogWarning("Unable to connect to host");

                // On failure connection
                OnClientFailedToConnectEvent?.Invoke();
            }

            // Socket status
            if (ClientObject.WorkSocket.Poll(-1, SelectMode.SelectWrite))
            {
                // true, if processing a Connect(EndPoint), and the connection has succeeded;
                // -or-
                // true if data can be sent;

                // Successful socket connection
                OnClientConnection();
            }
            else if (ClientObject.WorkSocket.Poll(-1, SelectMode.SelectRead))
            {
                // true if Listen(Int32) has been called and a connection is pending;
                // -or-
                // true if data is available for reading;
                // -or-
                // true if the connection has been closed, reset, or terminated;
            }
            else if (ClientObject.WorkSocket.Poll(-1, SelectMode.SelectError))
            {
                Debug.LogError("This Socket has an error.");
            }
        }
        catch (SocketException e)
        {
            Debug.LogError("WinSock Error: " + e.ToString());
        }
    }

    /// <summary>
    /// Manages when a socket client is accepted by the sever
    /// </summary>
    /// <param name="result"></param>Asynchronous data
    private void OnAcceptConnection(IAsyncResult result)
    {
        Socket WorkSocket = (Socket)result.AsyncState;
        Socket AcceptedSocket = null;

        try
        {
            // Get connected socket
            AcceptedSocket = ListeningSocket.EndAccept(result);
        }
        catch (ObjectDisposedException e)
        {
            Debug.LogError("Server cannot accept the connection");
            Debug.LogError("WinSock Error: " + e.Message.ToString());
        }

        if(AcceptedSocket != null)
        {
            // Check if we reach the client limit
            int ClientsConnected = ServerSockets.Count;
            ClientsConnected += 1;

            if(ClientsConnected > NumOfClients)
            {
                Debug.Log("Max Num Of Clients Reached");

                // Note: Is not possible to reject a connection in Winsock C#
                // So, we accept it, notify the client and finally close the connection
                SocketObject SocketObj = new SocketObject();
                SocketObj.WorkSocket = AcceptedSocket;
                SocketObj.SendBuffer = new byte[MessageBufferSize];
                SocketObj.ReceiveBuffer= new byte[MessageBufferSize];

                AddClientLimitReachNotifyToQueue(ref SocketObj);
                ServerSendsTCPData(ref SocketObj);

                AcceptedSocket.Shutdown(SocketShutdown.Both);
                AcceptedSocket.Close();
            }
            else
            {
                if (IsConnectionDebug)
                    Debug.Log("Server Accepts Connection");

                // Create new socket object
                SocketObject SocketObj = new SocketObject();
                SocketObj.NetID = NetID++;
                SocketObj.WorkSocket = AcceptedSocket;
                SocketObj.SendBuffer = new byte[MessageBufferSize];
                SocketObj.ReceiveBuffer = new byte[MessageBufferSize];

                // Add to the connected sockets list
                ServerSockets.Add(SocketObj);

                // Using the RemoteEndPoint property.
                if (IsConnectionDebug)
                    Debug.Log("I am connected to " + IPAddress.Parse(((IPEndPoint)AcceptedSocket.RemoteEndPoint).Address.ToString()) +
                            " on port number: " + ((IPEndPoint)AcceptedSocket.RemoteEndPoint).Port.ToString());

                // Using the LocalEndPoint property.
                if (IsConnectionDebug)
                    Debug.Log("My local IP Address is : " + IPAddress.Parse(((IPEndPoint)AcceptedSocket.LocalEndPoint).Address.ToString()) +
                            " I am connected on port number: " + ((IPEndPoint)AcceptedSocket.LocalEndPoint).Port.ToString());

                // Initialize connection socket that will be in the network manager
                InitServerSocket(ref SocketObj);
            }
        }

        if(!ServerIsClosing)
        {
            // Continue accepting connections until the server is closed
            WorkSocket.BeginAccept(OnAcceptCallBack, WorkSocket);   
        }
    }

    /// <summary>
    /// Manages when client sends data to the server
    /// </summary>
    /// <param name="result"></param>Asynchronous data
    private void OnClientSendsTCPData(IAsyncResult result)
    {
        if(IsDebugMode)
            Debug.Log("Cliente mando data");

        // Ends the send task from the current working socket
        SocketObject SocketObj = (SocketObject)result.AsyncState;
        SocketObj.WorkSocket.EndSend(result);

        // Clean buffer
        Array.Clear(SocketObj.SendBuffer, 0, SocketObj.SendBuffer.Length);
    }

    /// <summary>
    /// Manages when client receives data from the server
    /// </summary>
    /// <param name="result"></param>Asynchronous data
    private void OnClientReceivesTCPData(IAsyncResult result)
    {
        if (IsDebugMode)
            Debug.Log("Cliente recibio data");

        SocketObject SocketObj = (SocketObject)result.AsyncState;

        // Ends the receive task from the current working socket
        int ReadData = SocketObj.WorkSocket.EndReceive(result);
        if (ReadData > 0)
        {
            // The subscribed buffer to the BeginReceive is the one that receives the transfered bytes,
            // moreover its size is what the user assigns to it in the buffer size variable

            // Unpackage and save data to read it later in the main thread
            Package ReceivedPackage = PoolPackage.GetPackage();
            ReceivedPackage.AddData(ref SocketObj.ReceiveBuffer);
            ReceivedPackage.IsFree = false;

            // Clean buffer
            Array.Clear(SocketObj.ReceiveBuffer, 0, SocketObj.ReceiveBuffer.Length);

            // Continue receiving data
            SocketObj.WorkSocket.BeginReceive(SocketObj.ReceiveBuffer, 0, MessageBufferSize,
                SocketFlags.None, OnClientReceivesTCPCallBack, SocketObj);
        }
        else
        {
            // Server shuts down
            OnServerClosedEvent?.Invoke();

            // Close the client socket
            CloseTCPSocket(ref ClientObject);
        }
    }

    /// <summary>
    /// Manages when server sends data to a client socket
    /// </summary>
    /// <param name="result"></param>Asynchronous data
    private void OnServerSendsTCPData(IAsyncResult result)
    {
        if (IsDebugMode)
            Debug.Log("Server mando data");

        // Ends the send task from the current working socket
        SocketObject SocketObj = (SocketObject)result.AsyncState;
        SocketObj.WorkSocket.EndSend(result);

        // Clean buffer
        Array.Clear(SocketObj.SendBuffer, 0, SocketObj.SendBuffer.Length);
    }

    /// <summary>
    /// Manages when server receives data from a client
    /// </summary>
    /// <param name="result"></param>Asynchronous data
    private void OnServerReceivesTCPData(IAsyncResult result)
    {
        if (IsDebugMode)
            Debug.Log("Server recibio data");

        SocketObject SocketObj = (SocketObject)result.AsyncState;

        // Ends the receive task from the current working socket
        int ReadData = SocketObj.WorkSocket.EndReceive(result);
        if (ReadData > 0)
        {
            // The subscribed buffer to the BeginReceive is the one that receives the transfered bytes,
            // moreover its size is what the user assigns to it in the buffer size variable

            // Unpackage and save data to read it later in the main thread
            Package ReceivedPackage = PoolPackage.GetPackage();
            ReceivedPackage.AddData(ref SocketObj.ReceiveBuffer);
            ReceivedPackage.IsFree = false;

            // Clean buffer
            Array.Clear(SocketObj.ReceiveBuffer, 0, SocketObj.ReceiveBuffer.Length);

            // Continue receiving data
            SocketObj.WorkSocket.BeginReceive(SocketObj.ReceiveBuffer, 0, MessageBufferSize,
                SocketFlags.None, OnServerReceivesTCPCallBack, SocketObj);
        }
        else
        {
            // Socket disconnection
            if (IsConnectionDebug)
                Debug.Log("Disconnection - Socket NetID: " + SocketObj.NetID);

            // Remove from ready sockets
            RemoveSocketFromReadySockets(SocketObj.NetID);

            // Destroy player object and remove player data
            Destroy(Players[SocketObj.NetID].PlayerGameObj);
            Players.Remove(SocketObj.NetID);

            // Call client disconnection event
            OnClientDisconnectedEvent?.Invoke();

            // Close socket
            CloseTCPSocket(ref SocketObj);

            // Remove from socket list
            ServerSockets.Remove(SocketObj);

            // TODO: Posiblemente tenga que enviar evento
            // a los sockets restantes de que alguien se fue para eliminarlo
        }
    }

    /// <summary>
    /// Initializes server socket services
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    private void InitServerSocket(ref SocketObject _socketObj)
    {
        // Init the socket to start receiving data
        ServerStartReceivingTCPData(ref _socketObj);

        // Sends notification of the connection to the client
        AddInitialDataToQueue(ref _socketObj);
    }

    /// <summary>
    /// Manages when a client socket is connected with the server
    /// </summary>
    private void OnClientConnection()
    {
        if (IsConnectionDebug)
            Debug.Log("Socket connection made");

        // Set client flag as true
        IsClient = true;

        // Init the socket to start receiving data
        ClientRecievesTCPData();
    }

    /// <summary>
    /// True if there is data exchange between client and server
    /// </summary>
    /// <param name="_netID"></param>
    private void SetSocketAsActive(int _netID)
    {
        SocketObject Socket = GetSocketObjectByNetID(_netID);
        
        // Set socket as active 
        Socket.isActive = true;

        // Reset strkies from the socket
        Socket.ConnectionStrikes = 0;
    }
    #endregion

    #region [Message System]
    // Public Functions
    /// <summary>
    /// Adds client input in the message queue
    /// </summary>
    /// <param name="_inputType"></param>Input type
    public void AddClientInputToQueue(EInputType _inputType)
    {
        byte[] Buffer = new byte[HeaderMessageSize + InputMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.Input);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(InputMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(ClientObject.NetID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes((int)_inputType);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        AddDataToQueue(ref ClientObject, Buffer);
    }

    /// <summary>
    /// Adds start request in the message queue
    /// </summary>
    public void AddStartRequestToQueue()
    {
        byte[] Buffer = new byte[HeaderMessageSize + RequestToServerMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.RequestToServer);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(RequestToServerMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(ClientObject.NetID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes((int)ERequestType.StartGame);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        AddDataToQueue(ref ClientObject, Buffer);
    }

    /// <summary>
    /// Adds respawn request in the message queue
    /// </summary>
    public void AddRespawnRequestToQueue()
    {
        byte[] Buffer = new byte[HeaderMessageSize + RequestToServerMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.RequestToServer);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(RequestToServerMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(ClientObject.NetID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes((int)ERequestType.Respawn);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        AddDataToQueue(ref ClientObject, Buffer);
    }

    /// <summary>
    /// Adds ready to start updating with the server in the message queue
    /// </summary>
    public void AddReadyToUpdateToQueue()
    {
        byte[] Buffer = new byte[HeaderMessageSize + ReadyToUpdateMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.ReadyToUpdate);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(ReadyToUpdateMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(ClientObject.NetID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        AddDataToQueue(ref ClientObject, Buffer);
    }

    /// <summary>
    /// Notifies Game Mode to spawn an obstacle
    /// </summary>
    /// <param name="_pathType"></param>Path selected
    public void SendSpawnBubble(int _pathType)
    {
        // Spawn bubble in all clients
        SendSpawnBubbleMulticast(_pathType);

        // Spawn bubble in server
        CS_GameMode.Instance.SpawnBubble(_pathType);
    }

    /// <summary>
    /// Notifies all players a bullet collision
    /// </summary>
    /// <param name="_playerID"></param>Game ID
    /// <param name="_bulletID"></param>Bullet ID
    /// <param name="_bubbleID"></param>Bubble ID
    public void SendBulletCollisionMessage(int _playerID, uint _bulletID, uint _bubbleID)
    {
        if (IsDebugMode)
            Debug.Log("Collision Data -> Player ID: " + _playerID + " bullet ID: " + _bulletID + " bubble ID: " + _bubbleID);

        // Send bullet collision in all clients
        int PlayerNetID = GetNetIDByGameID(_playerID);

        if (PlayerNetID != -1)
        {
            SendBulletCollisionMulticast(PlayerNetID, _bulletID, _bubbleID);

            // Bullet collision in server
            DestroyObstacle(_bubbleID);
            DestroyBullet(PlayerNetID, _bulletID);
        }
    }

    /// <summary>
    /// Notifies all players a ship(player) collision
    /// </summary>
    /// <param name="_playerID"></param>Game ID
    /// <param name="_bubbleID"></param>Bubble ID
    public void SendShipCollisionMessage(int _playerID, uint _bubbleID)
    {
        if (IsDebugMode)
            Debug.Log("Collision Data-> Player ID: " + _playerID + " bubble ID: " + _bubbleID);

        int CollidedShipNetID = GetNetIDByGameID(_playerID);

        if(CollidedShipNetID != -1)
        {
            SPlayerData PlayerData = Players[CollidedShipNetID];

            // Update sync lives
            PlayerData.NumOfLives -= 1;
            Players[CollidedShipNetID] = PlayerData;

            // Send ship collision in all clients
            SendShipCollisionMulticast(CollidedShipNetID, _bubbleID);

            // Ship collision in server
            DestroyObstacle(_bubbleID);
            DestroyShip(CollidedShipNetID, PlayerData.NumOfLives);
        }
    }

    /// <summary>
    /// Notifies all players and Game Mode a state change
    /// </summary>
    /// <param name="_state"></param>
    public void SendGameStateMessage(EGameStates _state)
    {
        // Change state in clients
        SendStateGameMulticast(_state);

        // Change state in server
        CS_GameMode.Instance.ChangeState(_state);
    }

    // Private Functions
    /// <summary>
    /// Packages all the messages to sends in the correct format
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    /// <returns></returns>True if there is data to package
    private bool PackageData(ref SocketObject _socketObj)
    {
        if(_socketObj.Messages.Count > 0)
        {
            // Init byte counter
            int Index = 0;

            // Add to the sending buffer the num of messages as first data
            byte[] bytes = BitConverter.GetBytes(_socketObj.Messages.Count);
            bytes.CopyTo(_socketObj.SendBuffer, Index);
            Index = bytes.Length;

            // Add all the messgaes to the queue buffer to send
            for (int i = 0; i < _socketObj.Messages.Count; ++i)
            {
                _socketObj.Messages[i].CopyTo(_socketObj.SendBuffer, Index);
                Index += _socketObj.Messages[i].Length;
            }

            // Clean messages
            _socketObj.Messages.Clear();

            return true;
        }
        return false;
    }

    /// <summary>
    /// Server checks all the messages received from clients
    /// </summary>
    private void CheckServerMessages()
    {
        // Get all packages in the pool
        List<Package> Packages = PoolPackage.GetPackages();
        foreach (Package CurrentPackage in Packages)
        {
            // Check the ones that have data
            if(!CurrentPackage.IsRead && !CurrentPackage.IsFree)
            {
                byte[] Data = CurrentPackage.Data;

                int Index = 0;
                int NumOfMessages = BitConverter.ToInt32(Data, Index);
                Index = sizeof(int);

                if (NumOfMessages > 0)
                {
                    if (IsDebugMode)
                        Debug.Log(NumOfMessages);

                    for (int j = 0; j < NumOfMessages; ++j)
                    {
                        // Type
                        int DataType = BitConverter.ToInt32(Data, Index);
                        Index += sizeof(int);

                        if (IsDebugMode)
                            Debug.Log("Message Type: " + DataType);

                        // Size
                        int Size = BitConverter.ToInt32(Data, Index);
                        Index += sizeof(int);

                        // Message
                        if (DataType == (int)ENetToken.RequestToServer)
                        {
                            int ClientNetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            int RequestType = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            if (RequestType == (int)ERequestType.Synchronize)
                            {
                                // Send sync data to the socket that request it
                                SocketObject SocketObj = GetSocketObjectByNetID(ClientNetID);
                                AddSyncDataToQueue(ref SocketObj);
                            }
                            else if (RequestType == (int)ERequestType.Spawn)
                            {
                                // Spawn in server
                                ServerSpawnPlayer(ClientNetID);

                                // Send spawn to all
                                SendSpawnMulticast(ClientNetID);
                            }
                            else if (RequestType == (int)ERequestType.StartGame)
                            {
                                // Set this player as ready in the game mode
                                CS_GameMode.Instance.SetPlayerReady();

                                // If all the players are ready
                                if (CS_GameMode.Instance.GetNumOfPlayersReady() == NumOfClients)
                                {
                                    // Start game in the server
                                    CS_GameMode.Instance.ChangeState(EGameStates.START_GAME);

                                    // Multicast start game
                                    SendStateGameMulticast(EGameStates.START_GAME);
                                }
                            }
                            else if (RequestType == (int)ERequestType.Respawn)
                            {
                                // Check if the player can respawn
                                if(Players[ClientNetID].NumOfLives >= 0)
                                {
                                    // Send respawn to all clients
                                    SendRespawnMulticast(ClientNetID, Players[ClientNetID].GameID);

                                    // Respawn in server
                                    Vector2 RespawnPosition = CS_GameMode.Instance.GetStartPositionByGameID(Players[ClientNetID].GameID);
                                    RespawnShip(ClientNetID, RespawnPosition.x, RespawnPosition.y);

                                    // Update sync data
                                    SPlayerData PlayerData = Players[ClientNetID];
                                    PlayerData.Position = RespawnPosition;
                                    Players[ClientNetID] = PlayerData;
                                }
                            }
                        }
                        else if(DataType == (int)ENetToken.ReadyToUpdate)
                        {
                            int ClientNetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // Add socket to the ready sockets
                            SocketObject Socket = GetSocketObjectByNetID(ClientNetID);
                            if(null != Socket)
                                ReadySockets.Add(Socket);
                        }
                        else if (DataType == (int)ENetToken.Ping)
                        {
                            int ClientNetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            SetSocketAsActive(ClientNetID);
                        }
                        else if (DataType == (int)ENetToken.Input)
                        {
                            int ClientNetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            int InputType = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            if (InputType == (int)EInputType.Shoot)
                            {
                                // Shoot in Server
                                PlayerShoot(ClientNetID);

                                // Send input to all
                                SendInputMulticast(ClientNetID, InputType);
                            }
                            else
                            {
                                // Move in server
                                MovePlayer(ClientNetID, InputType);
                            }
                        }
                    }
                }

            }

            // Package read
            CurrentPackage.IsRead = true;
        }

        // Update pool packages
        PoolPackage.UpdatePackages();
    }

    /// <summary>
    /// Client check all the messages received from the server
    /// </summary>
    private void CheckClientMessages()
    {
        // Get all packages in the pool
        List<Package> Packages = PoolPackage.GetPackages();
        foreach (Package CurrentPackage in Packages)
        {
            // Check the ones that have data
            if (!CurrentPackage.IsRead && !CurrentPackage.IsFree)
            {
                byte[] Data = CurrentPackage.Data;

                int Index = 0;
                int NumOfMessages = BitConverter.ToInt32(Data, Index);
                Index = sizeof(int);

                if (NumOfMessages > 0)
                {
                    if (IsDebugMode)
                        Debug.Log(NumOfMessages);

                    for (int j = 0; j < NumOfMessages; ++j)
                    {
                        // Type
                        int DataType = BitConverter.ToInt32(Data, Index);
                        Index += sizeof(int);

                        if (IsDebugMode)
                            Debug.Log("Message Type: " + DataType);

                        // Size
                        int Size = BitConverter.ToInt32(Data, Index);
                        Index += sizeof(int);

                        // Message
                        if (DataType == (int)ENetToken.SuccessfulConnection)
                        {
                            ClientObject.NetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // On successful connection
                            OnClientConnectedEvent?.Invoke();
                        }
                        else if (DataType == (int)ENetToken.ClientLimitReached)
                        {
                            // TODO: Poner un mensaje en el canvas para avisar al jugador que ya se llego al maximo
                            // de clientes permitidos y que lo intentes en unos minutos mas

                            // Close client socket
                            CloseTCPSocket(ref ClientObject);
                        }
                        else if (DataType == (int)ENetToken.Synchronize)
                        {
                            // Create all the clients connected
                            int NumberOfClients = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            for (int k = 0; k < NumberOfClients; ++k)
                            {
                                int ClientNetID = BitConverter.ToInt32(Data, Index);
                                Index += sizeof(int);

                                int GameID = BitConverter.ToInt32(Data, Index);
                                Index += sizeof(int);

                                float PosX = BitConverter.ToSingle(Data, Index);
                                Index += sizeof(float);

                                float PosY = BitConverter.ToSingle(Data, Index);
                                Index += sizeof(float);

                                int Score = BitConverter.ToInt32(Data, Index);
                                Index += sizeof(int);

                                int NumOfLives = BitConverter.ToInt32(Data, Index);
                                Index += sizeof(int);

                                // Check if the key already exists
                                if (Players.ContainsKey(ClientNetID))
                                {
                                    SPlayerData PlayerData = Players[ClientNetID];

                                    PlayerData.GameID = GameID;
                                    PlayerData.Position.x = PosX;
                                    PlayerData.Position.y = PosY;
                                    PlayerData.Score = Score;
                                    PlayerData.NumOfLives = NumOfLives;

                                    // Update data
                                    Players[ClientNetID] = PlayerData;

                                    // Sync data in the object
                                    SyncPlayerData(ClientNetID);
                                }
                                else // If not we create a new player
                                {
                                    SPlayerData PlayerData = new SPlayerData();

                                    PlayerData.GameID = GameID;
                                    PlayerData.Position.x = PosX;
                                    PlayerData.Position.y = PosY;
                                    PlayerData.Score = Score;
                                    PlayerData.NumOfLives = NumOfLives;

                                    // Add new data
                                    Players.Add(ClientNetID, PlayerData);

                                    // Spawn a player object
                                    SpawnPlayer(ClientNetID);
                                }
                            }
                        }
                        else if (DataType == (int)ENetToken.Ping)
                        {
                            int ClientID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            ReturnPing();
                        }
                        else if (DataType == (int)ENetToken.SpawnPlayer)
                        {
                            // Save player data arrived and spawn the player in the current client instance
                            SPlayerData PlayerData = new SPlayerData();

                            // Get Network ID
                            int ClientNetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // Get Game ID
                            PlayerData.GameID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // X Position
                            PlayerData.Position.x = BitConverter.ToSingle(Data, Index);
                            Index += sizeof(float);

                            // Y Position
                            PlayerData.Position.y = BitConverter.ToSingle(Data, Index);
                            Index += sizeof(float);

                            // Score
                            PlayerData.Score = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // Num Of Lives
                            PlayerData.NumOfLives = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // Add player data to the dictionary
                            Players.Add(ClientNetID, PlayerData);

                            // Spawn the current ID player
                            SpawnPlayer(ClientNetID);
                        }
                        else if (DataType == (int)ENetToken.Input)
                        {
                            // Get Network ID
                            int ClientNetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // Input type
                            int InputType = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            if (InputType == (int)EInputType.Shoot)
                            {
                                // Shoot in Server
                                PlayerShoot(ClientNetID);
                            }
                            else
                            {
                                // Move in server
                                MovePlayer(ClientNetID, InputType);
                            }
                        }
                        else if (DataType == (int)ENetToken.GameState)
                        {
                            // Get Game State
                            int GameState = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // Change gam state in client
                            if (GameState == (int)EGameStates.START_GAME)
                            {
                                CS_GameMode.Instance.ChangeState(EGameStates.START_GAME);
                            }
                            else if (GameState == (int)EGameStates.CHANGE_STAGE)
                            {
                                CS_GameMode.Instance.ChangeState(EGameStates.CHANGE_STAGE);
                            }
                            else if (GameState == (int)EGameStates.GAME)
                            {
                                CS_GameMode.Instance.ChangeState(EGameStates.GAME);
                            }
                            else if (GameState == (int)EGameStates.GAME_OVER)
                            {
                                // Set game over data to the local player 
                                CS_ShipPlayer LocalPlayer = Players[ClientObject.NetID].PlayerComponent;
                                LocalPlayer.GameOver();

                                // Change gam state
                                CS_GameMode.Instance.ChangeState(EGameStates.GAME_OVER);
                            }

                        }
                        else if (DataType == (int)ENetToken.SpawnBubble)
                        {
                            // Get Path type
                            int PathType = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // Spawn Bubble
                            CS_GameMode.Instance.SpawnBubble(PathType);
                        }
                        else if (DataType == (int)ENetToken.Collision)
                        {
                            // Get Network ID
                            int ClientNetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            int CollisionType = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            if (CollisionType == (int)ECollisionType.Bullet)
                            {
                                uint BulletID = BitConverter.ToUInt32(Data, Index);
                                Index += sizeof(uint);

                                uint BubbleID = BitConverter.ToUInt32(Data, Index);
                                Index += sizeof(uint);

                                DestroyObstacle(BubbleID);
                                DestroyBullet(ClientNetID, BulletID);
                            }
                            else if (CollisionType == (int)ECollisionType.Ship)
                            {
                                uint BubbleID = BitConverter.ToUInt32(Data, Index);
                                Index += sizeof(uint);

                                int NumOFLives = BitConverter.ToInt32(Data, Index);
                                Index += sizeof(int);

                                DestroyObstacle(BubbleID);
                                DestroyShip(ClientNetID, NumOFLives);
                            }
                        }
                        else if (DataType == (int)ENetToken.Respawn)
                        {
                            // Get Network ID
                            int ClientNetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // X Position
                            float PosX = BitConverter.ToSingle(Data, Index);
                            Index += sizeof(float);

                            // Y Position
                            float PosY = BitConverter.ToSingle(Data, Index);
                            Index += sizeof(float);

                            // Respawn
                            RespawnShip(ClientNetID, PosX, PosY);
                        }
                        else if(DataType == (int)ENetToken.PlayerPosition)
                        {
                            // Get Network ID
                            int ClientNetID = BitConverter.ToInt32(Data, Index);
                            Index += sizeof(int);

                            // X Position
                            float PosX = BitConverter.ToSingle(Data, Index);
                            Index += sizeof(float);

                            // Y Position
                            float PosY = BitConverter.ToSingle(Data, Index);
                            Index += sizeof(float);

                            // Update ship position
                            UpdateShipPosition(ClientNetID, PosX, PosY);
                        }
                    }
                }
            }

            // Package read
            CurrentPackage.IsRead = true;
        }

        // Update pool packages
        PoolPackage.UpdatePackages();
    }

    /// <summary>
    /// Adds game data in the message queue
    /// </summary>
    /// <param name="_socket"></param>Socket reference to work with
    /// <param name="_gameData"></param>Game data
    private void AddDataToQueue(ref SocketObject _socket, byte[] _gameData)
    {
        if(_socket != null)
        {
            _socket.MessageBufferSize += _gameData.Length;

            if (_socket.MessageBufferSize >= MessageBufferSize) 
            {
                // Send current messages
                if(IsClient)
                {
                    ClientSendsTCPData();
                }
                else if(IsServer)
                {
                    ServerSendsTCPData(ref _socket);
                }

                // Queue the current message that triggers the case
                _socket.MessageBufferSize = _gameData.Length;
                _socket.Messages.Add(_gameData);
            }
            else
            {
                // Queue message
                _socket.Messages.Add(_gameData);
            }
        }
    }

    /// <summary>
    /// Adds initial game data in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    private void AddInitialDataToQueue(ref SocketObject _socketObj)
    {
        byte[] Buffer = new byte[HeaderMessageSize + SuccessfulConnectionMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.SuccessfulConnection);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(SuccessfulConnectionMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(_socketObj.NetID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds players limit reach notification in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    private void AddClientLimitReachNotifyToQueue(ref SocketObject _socketObj)
    {
        byte[] Buffer = new byte[HeaderMessageSize + ClientLimitReachedMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.ClientLimitReached);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(ClientLimitReachedMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds server request in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    /// <param name="_type"></param>Server request type
    private void AddServerRequestToQueue(ref SocketObject _socketObj, ERequestType _type)
    {
        byte[] Buffer = new byte[HeaderMessageSize + RequestToServerMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.RequestToServer);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(RequestToServerMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(_socketObj.NetID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes((int)_type);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds ping in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    private void AddPingToQueue(ref SocketObject _socketObj)
    {
        byte[] Buffer = new byte[HeaderMessageSize + PingMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.Ping);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(PingMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(_socketObj.NetID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds synchronization data in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    private void AddSyncDataToQueue(ref SocketObject _socketObj)
    {
        if(Players.Count > 0)
        {
            byte[] Buffer = new byte[SyncHeaderMessageSize + 
                (SynchronizMessageSize * Players.Count)];
            int Index = 0;

            // Token type
            byte[] bytes = BitConverter.GetBytes((int)ENetToken.Synchronize);
            bytes.CopyTo(Buffer, Index);
            Index += bytes.Length;

            // Size 
            bytes = BitConverter.GetBytes(SynchronizMessageSize * Players.Count);
            bytes.CopyTo(Buffer, Index);
            Index += bytes.Length;

            // Num of players
            bytes = BitConverter.GetBytes(Players.Count);
            bytes.CopyTo(Buffer, Index);
            Index += bytes.Length;

            //Message
            foreach (var item in Players)
            {
                // Network ID
                bytes = BitConverter.GetBytes(item.Key);
                bytes.CopyTo(Buffer, Index);
                Index += bytes.Length;

                // Player ID (Game)
                bytes = BitConverter.GetBytes(item.Value.GameID);
                bytes.CopyTo(Buffer, Index);
                Index += bytes.Length;

                // X Position
                bytes = BitConverter.GetBytes(item.Value.Position.x);
                bytes.CopyTo(Buffer, Index);
                Index += bytes.Length;

                // Y Position
                bytes = BitConverter.GetBytes(item.Value.Position.y);
                bytes.CopyTo(Buffer, Index);
                Index += bytes.Length;

                // Score
                bytes = BitConverter.GetBytes(item.Value.Score);
                bytes.CopyTo(Buffer, Index);
                Index += bytes.Length;

                // Num Of Lives
                bytes = BitConverter.GetBytes(item.Value.NumOfLives);
                bytes.CopyTo(Buffer, Index);
                Index += bytes.Length;
            }

            // Add to the queue
            AddDataToQueue(ref _socketObj, Buffer);
        }
    }

    /// <summary>
    /// Adds spawn player data in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    /// <param name="_clientNetID"></param>Client socket ID
    private void AddSpawnPlayerDataToQueue(ref SocketObject _socketObj, int _clientNetID)
    {
        byte[] Buffer = new byte[HeaderMessageSize + SpawnPlayerMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.SpawnPlayer);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(SpawnPlayerMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        SPlayerData Data = Players[_clientNetID];
        bytes = BitConverter.GetBytes(_clientNetID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(Data.GameID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(Data.Position.x);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(Data.Position.y);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(Data.Score);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(Data.NumOfLives);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds game state in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socker reference to work with
    /// <param name="_state"></param>Current game state
    private void AddGameStateToQueue(ref SocketObject _socketObj, EGameStates _state)
    {
        byte[] Buffer = new byte[HeaderMessageSize + GameStateMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.GameState);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(GameStateMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes((int)_state);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds spawn bubble in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    /// <param name="_pathType"></param>Path selected
    private void AddSpawnBubbleToQueue(ref SocketObject _socketObj, int _pathType)
    {
        byte[] Buffer = new byte[HeaderMessageSize + SpawnBubbleMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.SpawnBubble);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(SpawnBubbleMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(_pathType);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds bullet collision in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socklet reference to work with
    /// <param name="_netID"></param> Client socket ID
    /// <param name="_bulletID"></param>Bullet Id
    /// <param name="_bubbleID"></param>Bubble ID
    private void AddBulletCollisionToQueue(ref SocketObject _socketObj, int _netID, uint _bulletID, uint _bubbleID)
    {
        byte[] Buffer = new byte[HeaderMessageSize + BulletCollisionMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.Collision);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(BulletCollisionMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(_netID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes((int)ECollisionType.Bullet);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(_bulletID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(_bubbleID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds ship collision in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    /// <param name="_collidedNetID"></param>Client socket ID of the collided player(ship)
    /// <param name="_bubbleID"></param>Bubble ID
    /// <param name="_numOfLives"></param>Updated number of lives
    private void AddShipCollisionToQueue(ref SocketObject _socketObj, int _collidedNetID, uint _bubbleID, int _numOfLives)
    {
        byte[] Buffer = new byte[HeaderMessageSize + ShipCollisionMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.Collision);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(ShipCollisionMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(_collidedNetID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes((int)ECollisionType.Ship);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(_bubbleID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(_numOfLives);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds player respawn in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_posX"></param>Respawn X position
    /// <param name="_posY"></param>Respawn Y position
    private void AddRespawnToQueue(ref SocketObject _socketObj, int _netID, float _posX, float _posY)
    {
        byte[] Buffer = new byte[HeaderMessageSize + ShipRespawnMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.Respawn);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(ShipRespawnMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(_netID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(_posX);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(_posY);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds client input in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_input"></param>Client input ID
    private void AddInputToQueue(ref SocketObject _socketObj, int _netID, int _input)
    {
        byte[] Buffer = new byte[HeaderMessageSize + InputMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.Input);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(InputMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(_netID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(_input);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }

    /// <summary>
    /// Adds updated position in the message queue
    /// </summary>
    /// <param name="_socketObj"></param>Socket reference to work with
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_position"></param>Updated player position
    private void AddPositionToQueue(ref SocketObject _socketObj, int _netID, Vector2 _position)
    {
        byte[] Buffer = new byte[HeaderMessageSize + PlayerPositionMessageSize];
        int Index = 0;

        // Token type
        byte[] bytes = BitConverter.GetBytes((int)ENetToken.PlayerPosition);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Size 
        bytes = BitConverter.GetBytes(PlayerPositionMessageSize);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Message
        bytes = BitConverter.GetBytes(_netID);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(_position.x);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        bytes = BitConverter.GetBytes(_position.y);
        bytes.CopyTo(Buffer, Index);
        Index += bytes.Length;

        // Add to the queue
        AddDataToQueue(ref _socketObj, Buffer);
    }
    #endregion

    #region [Multicast]
    /// <summary>
    /// Sends spawn event to all clients
    /// </summary>
    /// <param name="_clietNetID"></param>Client socket ID
    private void SendSpawnMulticast(int _clietNetID)
    {
        for (int i = 0; i < ServerSockets.Count; i++)
        {
            SocketObject SocketObj = ServerSockets[i];
            AddSpawnPlayerDataToQueue(ref SocketObj, _clietNetID);
        } 
    }

    /// <summary>
    /// Sends synchronize event to all clients
    /// </summary>
    private void SendSyncrhornizeMulticast()
    {
        for (int i = 0; i < ReadySockets.Count; i++)
        {
            SocketObject SocketObj = ReadySockets[i];
            AddSyncDataToQueue(ref SocketObj);
        }
    }

    /// <summary>
    /// Sends current game state to all clients
    /// </summary>
    /// <param name="_state"></param>
    private void SendStateGameMulticast(EGameStates _state)
    {
        for (int i = 0; i < ReadySockets.Count; i++)
        {
            SocketObject SocketObj = ReadySockets[i];
            AddGameStateToQueue(ref SocketObj, _state);
        }
    }

    /// <summary>
    /// Sends spawn bubble event to all clients
    /// </summary>
    /// <param name="_pathType"></param>Path selected
    private void SendSpawnBubbleMulticast(int _pathType)
    {
        for (int i = 0; i < ReadySockets.Count; i++)
        {
            SocketObject SocketObj = ReadySockets[i];
            AddSpawnBubbleToQueue(ref SocketObj, _pathType);
        }
    }

    /// <summary>
    /// Sends bullet collision to all clients
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_bulletID"></param>Bullet ID
    /// <param name="_bubbleID"></param>Bubble ID
    private void SendBulletCollisionMulticast(int _netID, uint _bulletID, uint _bubbleID)
    {
        for (int i = 0; i < ReadySockets.Count; i++)
        {
            SocketObject SocketObj = ReadySockets[i];
            AddBulletCollisionToQueue(ref SocketObj, _netID, _bulletID, _bubbleID);
        }
    }

    /// <summary>
    /// Sends ship(player) collision to all clients
    /// </summary>
    /// <param name="_collidedNetID"></param>Client socket ID of the collided player(ship)
    /// <param name="_bubbleID"></param>Bubble ID
    private void SendShipCollisionMulticast(int _collidedNetID, uint _bubbleID)
    {
        SPlayerData Data = Players[_collidedNetID];

        for (int i = 0; i < ReadySockets.Count; i++)
        {
            SocketObject SocketObj = ReadySockets[i];
            AddShipCollisionToQueue(ref SocketObj, _collidedNetID, _bubbleID, Data.NumOfLives);
        }
    }

    /// <summary>
    /// Sends respawn event to all clients
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_playerID"></param>Game ID
    private void SendRespawnMulticast(int _netID, int _playerID)
    {
        Vector2 RespawnPosition = CS_GameMode.Instance.GetStartPositionByGameID(_playerID);
        for (int i = 0; i < ReadySockets.Count; i++)
        {
            SocketObject SocketObj = ReadySockets[i];
            AddRespawnToQueue(ref SocketObj, _netID, RespawnPosition.x, RespawnPosition.y);
        }
    }

    /// <summary>
    /// Sends client input to all the clients
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_input"></param>Input type
    private void SendInputMulticast(int _netID, int _input)
    {
        for (int i = 0; i < ReadySockets.Count; i++)
        {
            SocketObject SocketObj = ReadySockets[i];
            AddInputToQueue(ref SocketObj, _netID, _input);
        }
    }

    /// <summary>
    /// Sends updated player position to all clients
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_position"></param>Updated player position
    private void SendPositionMulticast(int _netID, Vector2 _position)
    {
        for (int i = 0; i < ReadySockets.Count; i++)
        {
            SocketObject SocketObj = ReadySockets[i];
            AddPositionToQueue(ref SocketObj, _netID, _position);
        }
    }
    #endregion

    #region [Latency System]
    /// <summary>
    /// Adds ping signal to the message queue
    /// </summary>
    private void StartPing()
    {
        IsPingActive = true;

        // Send ping to all the clients connected
        for (int i = 0; i < ServerSockets.Count; i++)
        {
            SocketObject SocketObj = ServerSockets[i];

            // Set all sockets as inactive
            // and the ones that returns the ping will change
            SocketObj.isActive = false;

            AddPingToQueue(ref SocketObj);
        }
    }

    /// <summary>
    /// Check if the clients are connected with the server via ping
    /// </summary>
    private void CheckPingActivity()
    {
        for (int i = 0; i < ServerSockets.Count; i++)
        {
            if (!ServerSockets[i].isActive)
                ServerSockets[i].ConnectionStrikes++;

            if (ServerSockets[i].ConnectionStrikes >= 3)
            {
                // Disconnect socket
                SocketObject SocketObj = ServerSockets[i];

                if (IsConnectionDebug)
                    Debug.Log("Disconnection - Socket NetID: " + SocketObj.NetID);

                // Remove from ready sockets
                RemoveSocketFromReadySockets(SocketObj.NetID);

                // Destroy player object and remove player data
                Destroy(Players[SocketObj.NetID].PlayerGameObj);
                Players.Remove(SocketObj.NetID);

                // Call client disconnection event
                OnClientDisconnectedEvent?.Invoke();

                // Close socket
                CloseTCPSocket(ref SocketObj);
            }
        }

        // Remove all inactive sockets with 3 strikes
        ServerSockets.RemoveAll(t => t.ConnectionStrikes >= 3);
    }

    /// <summary>
    /// Returns ping signal to the server
    /// </summary>
    private void ReturnPing()
    {
        AddPingToQueue(ref ClientObject);
    }
    #endregion

    #region [Game]
    /// <summary>
    /// Checks all player lives
    /// </summary>
    /// <returns></returns>True if a ship(player) still have lives
    public bool StillHaveLives()
    {
        foreach (var item in Players)
        {
            CS_ShipPlayer ShipComponent = item.Value.PlayerComponent;

            // If there is a ship with lives or alive the game continues
            if (ShipComponent.IsAlive || item.Value.NumOfLives >= 0)
                return true;
        }

        // Else, no one is alive
        return false;
    }

    /// <summary>
    /// Updates player position
    /// </summary>
    /// <param name="_playerID"></param>Game ID
    /// <param name="_position"></param>Current server position
    public void UpdatePosition(int _playerID, Vector3 _position)
    {
        int ClientNetID = GetNetIDByGameID(_playerID);

        if(ClientNetID != -1)
        {
            // Update sync data
            SPlayerData Data = Players[ClientNetID];
            Data.Position = _position;
            Players[ClientNetID] = Data;

            // Send position to all the clients
            SendPositionMulticast(ClientNetID, _position);
        }
    }

    /// <summary>
    /// Server creates player(ship) instance
    /// </summary>
    /// <param name="_clientNetID"></param>Client socket ID
    private void ServerSpawnPlayer(int _clientNetID)
    {
        SPlayerData Data = new SPlayerData();
        Data.GameID = CS_GameMode.Instance.GetPlayerID();
        Data.Score = InitialScore;
        Data.NumOfLives = InitialNumOfLives;
        Data.Position = CS_GameMode.Instance.GetStartPositionByGameID(Data.GameID);

        // Spawn player if we have prefab
        if (PlayerPrefab != null)
        {
            GameObject Player = Instantiate(PlayerPrefab);
            if (null != Player)
            {
                CS_ShipPlayer ShipComponent = Player.GetComponent<CS_ShipPlayer>();
                if (null != ShipComponent)
                {
                    Data.PlayerComponent = ShipComponent;
                    ShipComponent.Init(Data.GameID,
                                       Data.Score,
                                       Data.NumOfLives,
                                       Data.Position);
                }
                Data.PlayerGameObj = Player;
                Players.Add(_clientNetID, Data);
            }
        }
    }

    /// <summary>
    /// Client creates player(ship) instance
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    private void SpawnPlayer(int _netID)
    {
        if (PlayerPrefab != null)
        {
            SPlayerData Data = Players[_netID];
            GameObject Player = Instantiate(PlayerPrefab);

            if (null != Player)
            {
                CS_ShipPlayer ShipComponent = Player.GetComponent<CS_ShipPlayer>();
                if (null != ShipComponent)
                {
                    Data.PlayerComponent = ShipComponent;

                    if (ClientObject.NetID == _netID)
                        ShipComponent.IsLocalPlayer = true;

                    ShipComponent.Init(Players[_netID].GameID,
                                       Players[_netID].Score,
                                       Players[_netID].NumOfLives,
                                       Players[_netID].Position);
                }

                Data.PlayerGameObj = Player;
                Players[_netID] = Data;
            }
        }
    }

    /// <summary>
    /// Synchronizes the player's data with the server's data
    /// </summary>
    /// <param name="_netID"></param>
    private void SyncPlayerData(int _netID)
    {
        CS_ShipPlayer ShipComponent = Players[_netID].PlayerComponent;

        ShipComponent.SyncData(Players[_netID].GameID,
                               Players[_netID].Score,
                               Players[_netID].NumOfLives,
                               Players[_netID].Position);
    }

    /// <summary>
    /// Sets player move direction
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_inputType"></param>Client input type
    private void MovePlayer(int _netID, int _inputType)
    {
        CS_ShipPlayer ShipComponent = Players[_netID].PlayerComponent;
        ShipComponent.Move(_inputType);
    }

    /// <summary>
    /// Triggers player shoot
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    private void PlayerShoot(int _netID)
    {
        CS_ShipPlayer ShipComponent = Players[_netID].PlayerComponent;
        ShipComponent.Shoot();
    }

    /// <summary>
    /// Notifies Game Mode to return a bubble to the pool
    /// </summary>
    /// <param name="_bubbleID"></param>Bubble ID
    private void DestroyObstacle(uint _bubbleID)
    {
        CS_GameMode.Instance.DestroyObstacle(_bubbleID);
    }

    /// <summary>
    /// Notifies player to return a bullet to the pool
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_bulletID"></param>Bubble ID
    private void DestroyBullet(int _netID, uint _bulletID)
    {
        SPlayerData Data = Players[_netID];

        CS_ShipPlayer ShipComponent = Data.PlayerComponent;
        ShipComponent.DestroyBullet(_bulletID);

        // Update Score
        Data.Score += 1;
        ShipComponent.UpdateScore(Data.Score);
        Players[_netID] = Data;
    }

    /// <summary>
    /// Manages player die
    /// </summary>
    /// <param name="_collidedNetID"></param>Client socket ID of the collided player(ship)
    /// <param name="_numOfLives"></param>Number of current lives
    private void DestroyShip(int _collidedNetID, int _numOfLives)
    {
        CS_ShipPlayer ShipComponent = Players[_collidedNetID].PlayerComponent;
        ShipComponent.Die(_numOfLives);
    }

    /// <summary>
    /// Manages player respawn
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_posX"></param>Respawn X position
    /// <param name="_posY"></param>Respawn Y position
    private void RespawnShip(int _netID, float _posX, float _posY)
    {
        CS_ShipPlayer ShipComponent = Players[_netID].PlayerComponent;
        ShipComponent.Respawn(_posX, _posY);
    }

    /// <summary>
    /// Fixes client position with the updated server position
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    /// <param name="_posX"></param>Server X position
    /// <param name="_posY"></param>Server Y position
    private void UpdateShipPosition(int _netID, float _posX, float _posY)
    {
        CS_ShipPlayer ShipComponent = Players[_netID].PlayerComponent;
        ShipComponent.FixPosition(_posX, _posY);
    }
    #endregion

    #region [Events]
    /// <summary>
    /// Handles client connection
    /// </summary>
    void OnClientConnectedHandler()
    {
        if (IsConnectionDebug)
            Debug.Log("Successful connection");

        // Add message to spawn
        AddServerRequestToQueue(ref ClientObject, ERequestType.Synchronize);
        AddServerRequestToQueue(ref ClientObject, ERequestType.Spawn);

        // Set client as connected
        IsConnected = true;
    }

    /// <summary>
    /// Handles failed client connection
    /// </summary>
    void OnClientFailedToConnectHandler()
    {
        if (IsConnectionDebug)
            Debug.LogError("Failed Connection");
    }

    /// <summary>
    /// Handles client disconnection
    /// </summary>
    void OnClientDisconnectedHandler()
    {
        if (IsConnectionDebug)
            Debug.Log("Client Disconnected");

        CS_GameMode.Instance.SubstractPlayer();
    }

    /// <summary>
    /// Handles server start up
    /// </summary>
    void OnServerStartedHandler()
    {
        if (IsConnectionDebug)
            Debug.Log("Server Started");

        // Set server flag as true
        IsServer = true;
    }

    /// <summary>
    /// Handles server shut down
    /// </summary>
    void OnServerClosedHandler()
    {
        if (IsConnectionDebug)
            Debug.Log("Server Shuts Down");
        IsConnected = false;
    }
    #endregion

    #region [Misc]
    /// <summary>
    /// Obtains socket object by client socket ID
    /// </summary>
    /// <param name="_netID"></param>Client socket ID
    /// <returns></returns>Socket object
    private SocketObject GetSocketObjectByNetID(int _netID)
    {
        foreach (var SocketItem in ServerSockets)
        {
            if (_netID == SocketItem.NetID)
                return SocketItem;
        }

        return null;
    }

    /// <summary>
    /// Obtains client net ID by game ID
    /// </summary>
    /// <param name="_gameID"></param>Game ID
    /// <returns></returns>Net ID
    private int GetNetIDByGameID(int _gameID)
    {
        foreach (var item in Players)
        {
            if (_gameID == item.Value.GameID)
                return item.Key;
        }

        return -1;
    }

    /// <summary>
    /// Removes socket object from the ready list sockets
    /// </summary>
    /// <param name="_netID"></param> Client net ID
    private void RemoveSocketFromReadySockets(int _netID)
    {
        foreach(var item in ReadySockets)
        {
            if(item.NetID == _netID)
                ReadySockets.Remove(item);
        }
    }
    #endregion

    #region [Static Functions]
    /// <summary>
    /// Converts IP address to integer
    /// </summary>
    /// <param name="addr"></param>Valid IP Address
    /// <returns></returns>Integer address
    static UInt32 ConvertFromIpAddressToInteger(string addr)
    {
        var address = IPAddress.Parse(addr);
        byte[] bytes = address.GetAddressBytes();

        // flip big-endian(network order) to little-endian
        //if (BitConverter.IsLittleEndian)
        //{
        //    Array.Reverse(bytes);
        //}

        return BitConverter.ToUInt32(bytes, 0);
    }
    #endregion
}

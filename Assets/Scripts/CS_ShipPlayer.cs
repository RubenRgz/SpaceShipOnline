using System.Collections.Generic;
using System;
using UnityEngine.UI;
using UnityEngine;

public class CS_ShipPlayer : MonoBehaviour
{
    #region[Variables]
    public bool IsLocalPlayer { get; set; }
    public bool IsAlive { get; private set; }

    float MovementSpeed = 4.0f;
    public GameObject BulletPrefab;
    public Transform BulletSpawnSocket;
    int Score = 0;
    int TotalScore = 0;
    int NumOfLives = 0;
    int ID = 0;
    uint BulletID = 0;
    bool IsInputBlocker = false;
    float TimeToResetInputBlocker = 1.0f; // seconds
    float DeltaTimeToResetInputBlocker = 0.0f;
    float PositionLerpTime = 0.1f; // seconds
    float DeltaLerpToServerPosition = 0.0f;
    float DeltaLerpToLocalPosition = 0.0f;

    List<CS_Command> Commands = new List<CS_Command>();
    CS_LeftCommand LeftCommand = new CS_LeftCommand();
    CS_RightCommand RightCommand = new CS_RightCommand();
    CS_UpCommand UpCommand = new CS_UpCommand();
    CS_DownCommand DownCommand = new CS_DownCommand();
    CS_ShootCommand ShootCommand = new CS_ShootCommand();
    CS_PoolManager Bullets = null;

    List<GameObject> PoolObjects = new List<GameObject>();
    Vector2 Direction = new Vector2();
    Vector3 LastPosition = new Vector3();
    Vector3 ServerPosition = new Vector3();
    Text LivesHUD = null;
    Text ScoreHUD = null;
    #endregion

    #region[Unity]
    // Start is called before the first frame update
    void Start()
    {
        // Create instance of the pool manager and attach it to this object
        Bullets = gameObject.AddComponent<CS_PoolManager>();

        if (BulletPrefab != null)
            Bullets.Init(BulletPrefab, 30);

        if (CS_NetworkManager.Instance.IsClient && IsLocalPlayer)
        {
            // Activate button and UI for the local player
            if (CS_UIManager.Instance.ReadyButton != null)
            {
                CS_UIManager.Instance.ReadyButton.gameObject.SetActive(true);
                CS_UIManager.Instance.ReadyButton.onClick.AddListener(OnPlayerReady);
            }

            // Subscribe to cloud save service events
            CS_CloudSaveService.Instance.OnCloudDataRetrieved += OnCloudDataRetrievedHandler;

            // Check if service is ready
            if (CS_CloudSaveService.Instance.isOnline)
            {
                CS_CloudSaveService.Instance.GetCloudData(ECloudKeyData.TotalScore);
            }
            // Note: If not we can add a coroutine to try again
            // (but all the test done works without any extra code)

            // Record device data
            Unity.Services.Analytics.CS_AnalyticsService.RecordDeviceData();
            Unity.Services.Analytics.CS_AnalyticsService.FlushQueueData();
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (CS_NetworkManager.Instance.IsClient && IsLocalPlayer)
        {
            if(!IsInputBlocker)
                HandleInput();
            if(Commands.Count > 0)
            {
                foreach (var Command in Commands)
                {
                    GameObject PlayerObj = this.gameObject;
                    Command.Execute(ref PlayerObj);
                }

                // Clean commands
                Commands.Clear();
            }
        }
    }

    private void FixedUpdate()
    {
        if (IsInputBlocker)
        {
            DeltaTimeToResetInputBlocker += Time.fixedDeltaTime;

            if (DeltaTimeToResetInputBlocker >= TimeToResetInputBlocker)
            {
                DeltaTimeToResetInputBlocker = 0.0f;
                IsInputBlocker = false;
            }
        }
        else
        {
            if (CS_NetworkManager.Instance.IsClient)
            {
                if (ServerPosition != Vector3.zero)
                {
                    DeltaLerpToServerPosition += Time.fixedDeltaTime;

                    if (DeltaLerpToServerPosition >= PositionLerpTime)
                    {
                        transform.position = ServerPosition;
                        LastPosition = transform.position;
                        DeltaLerpToServerPosition -= PositionLerpTime;
                        ServerPosition = Vector3.zero;
                    }
                    else
                    {
                        float Time = DeltaLerpToServerPosition / PositionLerpTime;
                        transform.position = Vector3.Lerp(LastPosition, ServerPosition, Time);
                    }
                }
                else
                {
                    Vector2 Velocity = Direction * MovementSpeed * Time.fixedDeltaTime;
                    Vector3 NextPosition = LastPosition + new Vector3(Velocity.x, Velocity.y);

                    DeltaLerpToLocalPosition += Time.fixedDeltaTime;

                    if (DeltaLerpToLocalPosition >= PositionLerpTime)
                    {
                        transform.position = Vector3.Lerp(LastPosition, NextPosition, 1.0f);
                        LastPosition = transform.position;
                        DeltaLerpToLocalPosition -= PositionLerpTime;
                    }
                    else
                    {
                        float Time = DeltaLerpToLocalPosition / PositionLerpTime;
                        transform.position = Vector3.Lerp(LastPosition, NextPosition, Time);
                    }
                }
            }
            else if (CS_NetworkManager.Instance.IsServer)
            {
                Vector2 Velocity = Direction * MovementSpeed * Time.fixedDeltaTime;
                transform.position += new Vector3(Velocity.x, Velocity.y);

                CS_NetworkManager.Instance.UpdatePosition(ID, transform.position);
            }

            Direction = Vector2.zero;
        }
    }

    private void OnDestroy()
    {
        Bullets.Clean();
    }
    #endregion

    #region[Gameplay]
    /// <summary>
    /// Initializes player variables with server info
    /// </summary>
    /// <param name="_id"></param>Game ID of this player
    /// <param name="_score"></param>Current player score
    /// <param name="_numOfLives"></param>Current num of lives
    /// <param name="_position"></param>Start position
    public void Init(int _id, int _score, int _numOfLives, Vector2 _position)
    {
        ID = _id;
        Score = _score;
        NumOfLives = _numOfLives;
        transform.position = _position;
        LastPosition = transform.position;

        if (CS_NetworkManager.Instance.IsServer || IsLocalPlayer)
        {
            // Get HUD elements
            GetPlayerHUD();

            // Init HUD info
            LivesHUD.text = "Lives: " + _numOfLives;
            ScoreHUD.text = "Score: " + _score;
        }

        // Notify Network Manager that this player is ready to be updated
        if(CS_NetworkManager.Instance.IsClient && IsLocalPlayer)
            CS_NetworkManager.Instance.AddReadyToUpdateToQueue();
    }

    /// <summary>
    /// Synchronizes the player with the information the serves has
    /// </summary>
    /// <param name="_id"></param>Game ID of this player
    /// <param name="_score"></param>Current player score
    /// <param name="_numOfLives"></param>Current num of lives
    /// <param name="_position"></param>Current player position
    public void SyncData(int _id, int _score, int _numOfLives, Vector2 _position)
    {
        ID = _id;
        Score = _score;
        NumOfLives = _numOfLives;
        ServerPosition = _position;
        LastPosition = transform.position;
    }

    /// <summary>
    /// Stores ship direction based on the user input
    /// </summary>
    /// <param name="_inputType"></param>
    public void Move(int _inputType)
    {
        if (_inputType == (int)EInputType.Left)
        {
            Direction.x = -1.0f;
            Direction.y = 0.0f;
        }
        else if (_inputType == (int)EInputType.Right)
        {
            Direction.x = 1.0f;
            Direction.y = 0.0f;
        }
        else if (_inputType == (int)EInputType.Up)
        {
            Direction.x = 0.0f;
            Direction.y = 1.0f;
        }
        else if (_inputType == (int)EInputType.Down)
        {
            Direction.x = 0.0f;
            Direction.y = -1.0f;
        }
    }

    /// <summary>
    /// Shoots a projectile
    /// </summary>
    public void Shoot()
    {
        if(BulletSpawnSocket != null)
        {
            GameObject Bullet = Bullets.GetPoolObject();
            
            if(Bullet != null)
            {
                CS_Bullet BulletComponent = Bullet.GetComponent<CS_Bullet>();
                if (null != BulletComponent)
                {
                    // Subscribe to event
                    BulletComponent.OnLifeTimeExpiredEvent += OnLifeTimeExpiredHandler;
                    // Set ID of who shoot the bullet
                    BulletComponent.PlayerID = ID;
                    // Set Bullet ID 
                    BulletComponent.ID = BulletID++;
                }

                Bullet.transform.position = BulletSpawnSocket.position;
                Bullet.SetActive(true);

                // Register object in the local pool list
                PoolObjects.Add(Bullet);
            }
        }
    }

    /// <summary>
    /// Handles what to do with the bullets when their life time is over
    /// </summary>
    /// <param name="_bullet"></param>
    private void OnLifeTimeExpiredHandler(CS_Bullet _bullet)
    {
        _bullet.OnLifeTimeExpiredEvent -= OnLifeTimeExpiredHandler;

        int DestroyIndex = -1;
        for (int i = 0; i < PoolObjects.Count; i++)
        {
            CS_Bullet BulletComponent = PoolObjects[i].GetComponent<CS_Bullet>();
            if (BulletComponent != null && BulletComponent.ID == _bullet.ID)
            {
                GameObject BulletObj = _bullet.gameObject;
                Bullets.ReturnPoolObject(ref BulletObj);
                DestroyIndex = i;
                break;
            }
        }

        if (DestroyIndex != -1)
        {
            // Remove object from list
            PoolObjects.RemoveAt(DestroyIndex);
        }
    }

    /// <summary>
    /// Manages when the user is ready to play
    /// </summary>
    public void OnPlayerReady()
    {
        // Send ready to the network manager
        CS_NetworkManager.Instance.AddStartRequestToQueue();

        // Hide button
        CS_UIManager.Instance.ReadyButton.gameObject.SetActive(false);
    }

    /// <summary>
    /// Returns the game ID of the player
    /// </summary>
    /// <returns></returns>Game ID
    public int GetPlayerID()
    {
        return ID;
    }

    /// <summary>
    /// Manages game logic when player dies
    /// </summary>
    /// <param name="_currentLives"></param>
    public void Die(int _currentLives)
    {
        // Hide object while die
        gameObject.SetActive(false);
        IsInputBlocker = true;

        // Update lives in the ship
        NumOfLives = _currentLives;

        // No lives left
        if (NumOfLives < 0)
        {
            IsAlive = false;
        }
        else
        {
            // Update HUD
            LivesHUD.text = "Lives: " + _currentLives;

            // Request respawn just if is client and is the local player
            if (CS_NetworkManager.Instance.IsClient && IsLocalPlayer)
                CS_NetworkManager.Instance.AddRespawnRequestToQueue();
        }
    }

    /// <summary>
    /// Manages when the player respawns to continue playing
    /// </summary>
    /// <param name="_posX"></param>Respawn X position
    /// <param name="_posY"></param>Respawn Y position
    public void Respawn(float _posX, float _posY)
    {
        gameObject.SetActive(true);

        Vector3 RespawnPosition = new Vector3(_posX, _posY);

        // Reset position variables
        DeltaLerpToServerPosition = 0.0f;
        DeltaLerpToLocalPosition = 0.0f;
        ServerPosition = Vector3.zero;
        transform.position = RespawnPosition;
        LastPosition = RespawnPosition;
    }

    /// <summary>
    /// Manages bullets destruction
    /// </summary>
    /// <param name="_bulletID"></param>
    public void DestroyBullet(uint _bulletID)
    {
        int DestroyIndex = -1;
        for (int i = 0; i < PoolObjects.Count; i++)
        {
            CS_Bullet BulletComponent = PoolObjects[i].GetComponent<CS_Bullet>();
            if (BulletComponent != null && BulletComponent.ID == _bulletID)
            {
                GameObject BulletObj = PoolObjects[i];
                Bullets.ReturnPoolObject(ref BulletObj);
                DestroyIndex = i;
                break;
            }
        }

        if(DestroyIndex != -1)
        {
            // Remove object from list
            PoolObjects.RemoveAt(DestroyIndex);
        }
    }

    /// <summary>
    /// Updates player score
    /// </summary>
    /// <param name="_score"></param>
    public void UpdateScore(int _score)
    {
        Score = _score;
        ScoreHUD.text = "Score: " + _score;
    }

    /// <summary>
    /// Fixes player position with the one in the server
    /// </summary>
    /// <param name="_posX"></param>Server X position
    /// <param name="_posY"></param>Server Y position
    public void FixPosition(float _posX, float _posY)
    {
        // Save server position to update it in the fixed update
        // And set the current position as last position
        ServerPosition = new Vector3(_posX, _posY);
        LastPosition = transform.position;
    }

    /// <summary>
    /// Manages when the player loses
    /// </summary>
    public void GameOver()
    {
        int NewTotalScore = TotalScore + Score;
        CS_UIManager.Instance.SetScore(Score);
        CS_UIManager.Instance.SetTotalScore(NewTotalScore);
        CS_UIManager.Instance.SetPlayerID();

        // Save new total score in the cloud
        if (CS_CloudSaveService.Instance.isOnline)
            CS_CloudSaveService.Instance.SaveCloudData(ECloudKeyData.TotalScore, NewTotalScore.ToString());

        // Send Game Data
        Unity.Services.Analytics.CS_AnalyticsService.RecordGameData(Time.realtimeSinceStartup);
        Unity.Services.Analytics.CS_AnalyticsService.FlushQueueData();
    }

    /// <summary>
    /// Input handler
    /// </summary>
    private void HandleInput()
    {
        // Movement
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            Commands.Add(LeftCommand);
        }
        else if (Input.GetKey(KeyCode.RightArrow))
        {
            Commands.Add(RightCommand);
        }
        else if (Input.GetKey(KeyCode.UpArrow))
        {
            Commands.Add(UpCommand);
        }
        else if (Input.GetKey(KeyCode.DownArrow))
        {
            Commands.Add(DownCommand);
        }

        // Attacks
        if (Input.GetKeyDown(KeyCode.W))
        {
            Commands.Add(ShootCommand);
        }
    }

    /// <summary>
    /// Gets players HUD object from the UI manager
    /// </summary>
    private void GetPlayerHUD()
    {
        GameObject HUDObj = CS_UIManager.Instance.GetPlayerHUDByID(ID);
        if (null != HUDObj)
        {
            LivesHUD = HUDObj.transform.GetChild(1).GetComponent<Text>();
            ScoreHUD = HUDObj.transform.GetChild(2).GetComponent<Text>();
        }
    }

    /// <summary>
    /// Handles data received from the cloud save service 
    /// </summary>
    /// <param name="_key"></param>
    /// <param name="_value"></param>
    private void OnCloudDataRetrievedHandler(ECloudKeyData _key, string _value)
    {
        switch(_key)
        {
            case ECloudKeyData.TotalScore:

                if(_value == null)
                {
                    // Data is not found in the cloud (first time)
                    TotalScore = 0;
                }
                else if(Int32.TryParse(_value, out int _wholeNumber))
                {
                    TotalScore = _wholeNumber;
                }
                break;
            default:
                break;
        }
    }
    #endregion
}

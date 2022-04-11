using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Level info to share when is played
/// </summary>
public struct SLevelData
{
    public int ID;
    public int MaxNumOfObstacles;
    public float ObstaclesSpeed;
    public float TimeToSpawnObstacle;
}

/// <summary>
/// Finite game states
/// </summary>
public enum EGameStates
{
    NONE = 0,
    START_GAME,
    CHANGE_STAGE,
    GAME,
    GAME_OVER
}

public class CS_GameMode : MonoBehaviour
{
    #region [Variables]
    // Basic singleton to have access to the Game Mode
    public static CS_GameMode Instance { get; private set; }

    public Transform Player1Start;
    public Transform Player2Start;
    public Transform Player3Start;
    public Transform Player4Start;

    public GameObject BubblePrefab;

    static int PlayerID = 0;
    static int PlayersReady = 0;

    float SpawnTime = 5.0f;
    float SpawnTimeCounter = 0.0f;
    int SpawnCounter = 0;
    int MaxNumOfObstacles = 0;
    int CurrentStageID = 0;
    uint BubbleID = 0;
    bool isPresenting = false;
    // Counter to know if the obstacles finished their path or been destroyed
    int BubblesDone = 0;
    float BubbleSpeed = 1.0f;
    int CurrentLevel = -1;

    List<SLevelData> Levels = new List<SLevelData>();
    List<List<Vector2>> Paths = null;
    CS_PoolManager Bubbles = null;
    CS_BezierPath BezierPath = null;
    List<GameObject> PoolObjects = new List<GameObject>();

    EGameStates CurrentState;
    #endregion

    #region [Unity]
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
        // Create instance of the pool manager and attach it to this object
        Bubbles = gameObject.AddComponent<CS_PoolManager>();

        // Create instance of the bezier path and attach it to this object
        BezierPath = gameObject.AddComponent<CS_BezierPath>();

        if (BubblePrefab != null)
            Bubbles.Init(BubblePrefab, 50);

        // Create Bezier paths (This data can be serialized)
        Paths = new List<List<Vector2>>();
        CreateBezierPaths();

        // Create Levels (This data can be serialized)
        CreateLevels();

        // Set initial state
        CurrentState = EGameStates.NONE;
    }

    // Update is called once per frame
    void Update()
    {
        switch (CurrentState)
        {
            case EGameStates.NONE:
                break;
            case EGameStates.START_GAME:
                StartGame();
                break;
            case EGameStates.CHANGE_STAGE:
                if (!isPresenting)
                    SetLevel();
                break;
            case EGameStates.GAME:
                UpdateGame();
                break;
            case EGameStates.GAME_OVER:
                GameOver();
                break;
            default:
                break;
        }
    }

    private void FixedUpdate()
    {
        switch (CurrentState)
        {
            case EGameStates.NONE:
                break;
            case EGameStates.START_GAME:
                break;
            case EGameStates.CHANGE_STAGE:
                break;
            case EGameStates.GAME:
                if (CS_NetworkManager.Instance.IsServer)
                    TryToSpawnBubbles(Time.fixedDeltaTime);
                break;
            case EGameStates.GAME_OVER:
                break;
            default:
                break;
        }
    }
    #endregion

    #region [Gameplay]
    /// <summary>
    /// Changes game state
    /// </summary>
    /// <param name="_state"></param>State to change
    public void ChangeState(EGameStates _state)
    {
        CurrentState = _state;
    }

    /// <summary>
    /// Notifies server to change the game state
    /// </summary>
    private void StartGame()
    {
        if (CS_NetworkManager.Instance.IsServer)
            CS_NetworkManager.Instance.SendGameStateMessage(EGameStates.CHANGE_STAGE);
    }

    /// <summary>
    /// Notifies UI manager to show game over panel
    /// </summary>
    private void GameOver()
    {
        CS_UIManager.Instance.ShowGameOver();
    }

    /// <summary>
    /// Sets player game ID
    /// </summary>
    /// <returns></returns>Game ID
    public int GetPlayerID()
    {
        PlayerID++;
        return PlayerID;
    }

    /// <summary>
    /// Decreases Game ID to mantain always the same max game IDs
    /// </summary>
    public void SubstractPlayer()
    {
        PlayerID--;
    }

    /// <summary>
    /// Returns the start position of each player (4 max)
    /// </summary>
    /// <param name="_gameID"></param>Game ID
    /// <returns></returns>Start position
    public Vector2 GetStartPositionByGameID(int _gameID)
    {
        if(_gameID == 1)
        {
            return Player1Start.transform.position;
        }
        else if(_gameID == 2)
        {
            return Player2Start.transform.position;
        }
        else if(_gameID == 3)
        {
            return Player3Start.transform.position;
        }
        else if(_gameID == 4)
        {
            return Player4Start.transform.position;
        }

        return new Vector2();
    }

    /// <summary>
    /// Spawns obstacle
    /// </summary>
    /// <param name="_pathType"></param>Path that the bubble will travel
    public void SpawnBubble(int _pathType)
    {
        GameObject Bubble = Bubbles.GetPoolObject();

        if (Bubble != null)
        {
            CS_Bubble BubbleComponent = Bubble.GetComponent<CS_Bubble>();
            if (null != BubbleComponent)
            {
                BubbleComponent.Init(Paths[_pathType], BubbleSpeed);
                BubbleComponent.ID = BubbleID++;

                // Subscribe to event
                BubbleComponent.OnFinishPathEvent += OnFinishPathHandler;
            }
            Bubble.SetActive(true);

            // Register object in the local pool list
            PoolObjects.Add(Bubble);
        }
    }

    /// <summary>
    /// Notifies the server if is time to spawn an obstacle
    /// </summary>
    /// <param name="_deltaTime"></param>Fixed delta time
    private void TryToSpawnBubbles(float _deltaTime)
    {
        SpawnTimeCounter += _deltaTime;
        if (SpawnTimeCounter >= SpawnTime && SpawnCounter < MaxNumOfObstacles)
        {
            // Select path type and send it to all the clients
            int PathType = Random.Range(0, Paths.Count); 
            CS_NetworkManager.Instance.SendSpawnBubble(PathType);

            // Increment spawn counter
            SpawnCounter++;

            // Reset counter
            SpawnTimeCounter = 0.0f;
        }
    }

    /// <summary>
    /// Handles when a bubble ends its path
    /// </summary>
    /// <param name="_bubble"></param>Bubble ID
    private void OnFinishPathHandler(CS_Bubble _bubble)
    {
        _bubble.OnFinishPathEvent -= OnFinishPathHandler;

        for (int i = 0; i < PoolObjects.Count; i++)
        {
            CS_Bubble BubbleComponent = PoolObjects[i].GetComponent<CS_Bubble>();
            if (BubbleComponent != null && BubbleComponent.ID == _bubble.ID)
            {
                GameObject BubbleObj = _bubble.gameObject;
                PoolObjects.Remove(BubbleObj);
                Bubbles.ReturnPoolObject(ref BubbleObj);
                break;
            }
        }

        // Bubble done
        if (CS_NetworkManager.Instance.IsServer)
            BubblesDone++;
    }

    /// <summary>
    /// Return bubble to the pool manager
    /// </summary>
    /// <param name="_bubbleID"></param>Bubble ID
    public void DestroyObstacle(uint _bubbleID)
    {
        for (int i = 0; i < PoolObjects.Count; i++)
        {
            CS_Bubble BubbleComponent = PoolObjects[i].GetComponent<CS_Bubble>();
            if (BubbleComponent != null && BubbleComponent.ID == _bubbleID)
            {
                GameObject BubbleObj = PoolObjects[i];
                PoolObjects.Remove(BubbleObj);
                Bubbles.ReturnPoolObject(ref BubbleObj);
                break;
            }
        }

        // Bubble done
        if (CS_NetworkManager.Instance.IsServer)
            BubblesDone++;
    }

    /// <summary>
    /// Counts the number of players ready
    /// </summary>
    public void SetPlayerReady()
    {
        PlayersReady++;
    }

    /// <summary>
    /// Returns nomber of players that are ready to play
    /// </summary>
    /// <returns></returns>Value
    public int GetNumOfPlayersReady()
    {
        return PlayersReady;
    }

    /// <summary>
    /// Sets the level info the user is playing and reset important flags and variables
    /// </summary>
    private void SetLevel()
    {
        // Advance to the next level
        CurrentLevel++;

        CurrentStageID = Levels[CurrentLevel].ID;
        SpawnTime = Levels[CurrentLevel].TimeToSpawnObstacle;
        BubbleSpeed = Levels[CurrentLevel].ObstaclesSpeed;
        MaxNumOfObstacles = Levels[CurrentLevel].MaxNumOfObstacles;

        // Reset variables for the new level
        SpawnCounter = 0;
        BubblesDone = 0;

        // Clean local pool list if there is a lagging object
        if (PoolObjects.Count > 0)
        {
            for (int i = 0; i < PoolObjects.Count; i++)
            {
                GameObject BubbleObj = PoolObjects[i];
                Bubbles.ReturnPoolObject(ref BubbleObj);;
                
            }
            PoolObjects.Clear();
        }

        // Notify UI Manager to present stage
        isPresenting = true;
        CS_UIManager.Instance.PrepareToPresent(CurrentStageID);
    }

    /// <summary>
    /// Manages when the stage presentation ends
    /// </summary>
    public void FinishPresentation()
    {
        if (CS_NetworkManager.Instance.IsServer)
            CS_NetworkManager.Instance.SendGameStateMessage(EGameStates.GAME);
    }

    /// <summary>
    /// Main loop of the game
    /// </summary>
    private void UpdateGame()
    {
        isPresenting = false;
        if (CS_NetworkManager.Instance.IsServer)
        {
            // Check if the players still have lives
            if (!CS_NetworkManager.Instance.StillHaveLives())
                CS_NetworkManager.Instance.SendGameStateMessage(EGameStates.GAME_OVER);

            // If all obstacles finished their path or died -> change stage
            //Debug.Log(BubblesDone);
            if (BubblesDone >= Levels[CurrentLevel].MaxNumOfObstacles)
            {
                // Check if we can advance or is game over
                int NextLevel = CurrentLevel + 1;
                if (NextLevel >= Levels.Count)
                    CS_NetworkManager.Instance.SendGameStateMessage(EGameStates.GAME_OVER);
                else
                    CS_NetworkManager.Instance.SendGameStateMessage(EGameStates.CHANGE_STAGE);
            }
        }
    }
    #endregion

    #region [Serializable data]
    // TODO: Checar los paths en el viewer
    /// <summary>
    /// Creates all the obstacle paths with bezier curves
    /// </summary>
    private void CreateBezierPaths()
    {
        //
        // First
        //
        SBezierCurve Curve = new SBezierCurve();
        Curve.p0 = new Vector2(0.0f, 6.0f);
        Curve.p1 = new Vector2(0.0f, 6.0f);
        Curve.p2 = new Vector2(0.0f, -6.0f);
        Curve.p3 = new Vector2(0.0f, -6.0f);

        BezierPath.AddCurve(Curve, 5);

        // Add path to the list
        List<Vector2> Path1 = new List<Vector2>();
        BezierPath.Sample(ref Path1);
        Paths.Add(Path1);

        // Clean Bezier Path
        BezierPath.Clear();

        //
        // Second
        //
        Curve = new SBezierCurve();
        Curve.p0 = new Vector2(-5.0f, 6.0f);
        Curve.p1 = new Vector2(-5.0f, 6.0f);
        Curve.p2 = new Vector2(-5.0f, -6.0f);
        Curve.p3 = new Vector2(-5.0f, -6.0f);

        BezierPath.AddCurve(Curve, 5);

        // Add path to the list
        List<Vector2> Path2 = new List<Vector2>();
        BezierPath.Sample(ref Path2);
        Paths.Add(Path2);

        // Clean Bezier Path
        BezierPath.Clear();

        //
        // Third
        //
        Curve = new SBezierCurve();
        Curve.p0 = new Vector2(5.0f, 6.0f);
        Curve.p1 = new Vector2(5.0f, 6.0f);
        Curve.p2 = new Vector2(5.0f, -6.0f);
        Curve.p3 = new Vector2(5.0f, -6.0f);

        BezierPath.AddCurve(Curve, 5);

        // Add path to the list
        List<Vector2> Path3 = new List<Vector2>();
        BezierPath.Sample(ref Path3);
        Paths.Add(Path3);

        // Clean Bezier Path
        BezierPath.Clear();

        //
        // Fourth
        //
        Curve.p0 = new Vector2(-10.0f, 3.0f);
        Curve.p1 = new Vector2(1.8f, 5.3f);
        Curve.p2 = new Vector2(-3.5f, -5.8f);
        Curve.p3 = new Vector2(-7.0f, 0.0f);

        BezierPath.AddCurve(Curve, 20);

        Curve.p0 = new Vector2(-7.0f, 0.0f);
        Curve.p1 = new Vector2(-10.5f, 7.4f);
        Curve.p2 = new Vector2(5.8f, 5.6f);
        Curve.p3 = new Vector2(0.5f, -8.0f);

        BezierPath.AddCurve(Curve, 20);

        // Add path to the list
        List<Vector2> Path4 = new List<Vector2>();
        BezierPath.Sample(ref Path4);
        Paths.Add(Path4);

        // Clean Bezier Path
        BezierPath.Clear();

        //
        // Fifth
        //
        Curve.p0 = new Vector2(10.0f, 3.0f);
        Curve.p1 = new Vector2(-1.8f, 5.3f);
        Curve.p2 = new Vector2(3.5f, -5.8f);
        Curve.p3 = new Vector2(7.0f, 0.0f);

        BezierPath.AddCurve(Curve, 20);

        Curve.p0 = new Vector2(7.0f, 0.0f);
        Curve.p1 = new Vector2(10.5f, 7.4f);
        Curve.p2 = new Vector2(-5.8f, 5.6f);
        Curve.p3 = new Vector2(-0.5f, -8.0f);

        BezierPath.AddCurve(Curve, 20);

        // Add path to the list
        List<Vector2> Path5 = new List<Vector2>();
        BezierPath.Sample(ref Path5);
        Paths.Add(Path5);

        // Clean Bezier Path
        BezierPath.Clear();

        //
        // Sixth
        //
        Curve.p0 = new Vector2(-5.0f, 6.0f);
        Curve.p1 = new Vector2(-5.0f, 6.0f);
        Curve.p2 = new Vector2(0.0f, 3.0f);
        Curve.p3 = new Vector2(0.0f, 3.0f);

        BezierPath.AddCurve(Curve, 5);

        Curve.p0 = new Vector2(0.0f, 3.0f);
        Curve.p1 = new Vector2(0.0f, 3.0f);
        Curve.p2 = new Vector2(-5.0f, 0.0f);
        Curve.p3 = new Vector2(-5.0f, 0.0f);

        BezierPath.AddCurve(Curve, 5);

        Curve.p0 = new Vector2(-5.0f, 0.0f);
        Curve.p1 = new Vector2(-5.0f, 0.0f);
        Curve.p2 = new Vector2(0.0f, -3.0f);
        Curve.p3 = new Vector2(0.0f, -3.0f);

        BezierPath.AddCurve(Curve, 5);

        Curve.p0 = new Vector2(0.0f, -3.0f);
        Curve.p1 = new Vector2(0.0f, -3.0f);
        Curve.p2 = new Vector2(-5.0f, -6.0f);
        Curve.p3 = new Vector2(-5.0f, -6.0f);

        BezierPath.AddCurve(Curve, 5);

        // Add path to the list
        List<Vector2> Path6 = new List<Vector2>();
        BezierPath.Sample(ref Path6);
        Paths.Add(Path6);

        // Clean Bezier Path
        BezierPath.Clear();

        //
        // Seventh
        //
        Curve.p0 = new Vector2(5.0f, 6.0f);
        Curve.p1 = new Vector2(5.0f, 6.0f);
        Curve.p2 = new Vector2(0.0f, 3.0f);
        Curve.p3 = new Vector2(0.0f, 3.0f);

        BezierPath.AddCurve(Curve, 5);

        Curve.p0 = new Vector2(0.0f, 3.0f);
        Curve.p1 = new Vector2(0.0f, 3.0f);
        Curve.p2 = new Vector2(5.0f, 0.0f);
        Curve.p3 = new Vector2(5.0f, 0.0f);

        BezierPath.AddCurve(Curve, 5);

        Curve.p0 = new Vector2(5.0f, 0.0f);
        Curve.p1 = new Vector2(5.0f, 0.0f);
        Curve.p2 = new Vector2(0.0f, -3.0f);
        Curve.p3 = new Vector2(0.0f, -3.0f);

        BezierPath.AddCurve(Curve, 5);

        Curve.p0 = new Vector2(0.0f, -3.0f);
        Curve.p1 = new Vector2(0.0f, -3.0f);
        Curve.p2 = new Vector2(5.0f, -6.0f);
        Curve.p3 = new Vector2(5.0f, -6.0f);

        BezierPath.AddCurve(Curve, 5);

        // Add path to the list
        List<Vector2> Path7 = new List<Vector2>();
        BezierPath.Sample(ref Path7);
        Paths.Add(Path7);

        // Clean Bezier Path
        BezierPath.Clear();
    }

    /// <summary>
    /// Creates all the levels data
    /// </summary>
    private void CreateLevels()
    {
        SLevelData Data = new SLevelData();

        //Level 1
        Data.ID = 1;
        Data.MaxNumOfObstacles = 1;
        Data.ObstaclesSpeed = 80.0f;
        Data.TimeToSpawnObstacle = 5.0f;
        Levels.Add(Data);

        // Level 2
        Data.ID = 2;
        Data.MaxNumOfObstacles = 2;
        Data.ObstaclesSpeed = 100.0f;
        Data.TimeToSpawnObstacle = 3.0f;
        Levels.Add(Data);

        // Level 3
        Data.ID = 3;
        Data.MaxNumOfObstacles = 3;
        Data.ObstaclesSpeed = 150.0f;
        Data.TimeToSpawnObstacle = 2.0f;
        Levels.Add(Data);
    }
    #endregion
}

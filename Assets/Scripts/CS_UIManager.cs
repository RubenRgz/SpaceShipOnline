using UnityEngine;
using UnityEngine.UI;

public class CS_UIManager : MonoBehaviour
{
    #region [Variables]
    // Basic singleton to have access to the Game Mode
    public static CS_UIManager Instance { get; private set; }

    public GameObject HUD = null;
    public Button ReadyButton = null;
    public Text StageText = null;
    public GameObject GameOverPanel = null;
    public GameObject GameInfoPanel = null;

    bool NeedToPresent = false;
    float PresentCounter = 0.0f;
    float PresentTime = 2.0f;

    GameObject InfoPanel = null;
    GameObject Player1HUD = null;
    GameObject Player2HUD = null;
    GameObject Player3HUD = null;
    GameObject Player4HUD = null;

    Text ScoreText = null;
    Text TotalScoreText = null;
    Text PlayerIDText = null;
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
        if(null != HUD)
        {
            InfoPanel = HUD.transform.GetChild(0).gameObject;

            if(null != InfoPanel)
            {
                Player1HUD = InfoPanel.transform.GetChild(0).gameObject;
                Player2HUD = InfoPanel.transform.GetChild(1).gameObject;
                Player3HUD = InfoPanel.transform.GetChild(2).gameObject;
                Player4HUD = InfoPanel.transform.GetChild(3).gameObject;
            }
        }

        if(GameInfoPanel != null)
        {
            ScoreText = GameInfoPanel.transform.GetChild(0).GetComponent<Text>();
            TotalScoreText = GameInfoPanel.transform.GetChild(1).GetComponent<Text>();
            PlayerIDText = GameInfoPanel.transform.GetChild(2).GetComponent<Text>();
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void FixedUpdate()
    {
        if(NeedToPresent)
        {
            PresentCounter += Time.fixedDeltaTime;
            if(PresentCounter >= PresentTime)
            {
                StageText.gameObject.SetActive(false);
                CS_GameMode.Instance.FinishPresentation();

                NeedToPresent = false;
            }
        }
    }
    #endregion

    #region [Gameplay]
    /// <summary>
    /// Sets data to present the current stage
    /// </summary>
    /// <param name="_stageID"></param>ID of the current stage
    public void PrepareToPresent(int _stageID)
    {
        if(StageText != null)
        {
            StageText.text = "Stage: " + _stageID;
            StageText.gameObject.SetActive(true);
        }

        // Reset variables for the new level
        PresentCounter = 0.0f;
        NeedToPresent = true;
    }

    /// <summary>
    /// Shows game over panel
    /// </summary>
    public void ShowGameOver()
    {
        if(!GameOverPanel.activeSelf)
            GameOverPanel.SetActive(true);
    }

    /// <summary>
    /// Returns player HUD panel by player game ID
    /// </summary>
    /// <param name="_playerID"></param>Game ID
    /// <returns></returns>HUD panel
    public GameObject GetPlayerHUDByID(int _playerID)
    {
        if (_playerID == 1)
            return Player1HUD;
        if (_playerID == 2)
            return Player2HUD;
        if (_playerID == 3)
            return Player3HUD;
        if (_playerID == 4)
            return Player4HUD;
        else
            return null;
    }

    /// <summary>
    /// Sets current score in the game over panel
    /// </summary>
    /// <param name="_score"></param>Current score
    public void SetScore(int _score)
    {
        ScoreText.text = "Score: " + _score.ToString();
    }

    /// <summary>
    /// Sets current total score in the game over panel
    /// </summary>
    /// <param name="_score"></param>Current total score
    public void SetTotalScore(int _score)
    {
        TotalScoreText.text = "Total Score: " + _score.ToString();
    }

    /// <summary>
    /// Sets Cloud Player ID in the game over panel
    /// </summary>
    public void SetPlayerID()
    {
        if(CS_CloudSaveService.Instance.isOnline)
            PlayerIDText.text = "Player Cloud ID: " + CS_CloudSaveService.Instance.PlayerID;
    }
    #endregion
}

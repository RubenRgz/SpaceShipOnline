using UnityEngine.UI;
using UnityEngine;

public class CS_ConnectionMenu : MonoBehaviour
{
    #region [Variables]
    public GameObject ConnectionPanel = null;
    public InputField IPAddressField = null;
    public Toggle ServerClientToggle = null;
    public Button Connect = null;

    bool IsServer = false;
    string IPAddress = null;
    #endregion

    #region [Unity]
    // Start is called before the first frame update
    void Start()
    {
        if(null != IPAddressField)
            IPAddressField.onValueChanged.AddListener(OnIPAddressChanged);

        if (null != ServerClientToggle)
            ServerClientToggle.onValueChanged.AddListener(OnIsServerChanged);

        if (null != Connect)
            Connect.onClick.AddListener(OnConnectClicked);
    }

    private void OnDestroy()
    {
        if (IsServer)
            CS_NetworkManager.Instance.OnServerStartedEvent -= OnServerStartedHandler;
        else
            CS_NetworkManager.Instance.OnClientConnectedEvent -= OnClientConnectedHandler;
    }
    #endregion

    #region [Callbacks]
    /// <summary>
    /// Stores Ip Address to connect to
    /// </summary>
    /// <param name="_ipAddress"></param>Address
    private void OnIPAddressChanged(string _ipAddress)
    {
        IPAddress = _ipAddress;
    }

    /// <summary>
    /// Stores if we want to run server or client logic
    /// </summary>
    /// <param name="_value"></param>Flag
    private void OnIsServerChanged(bool _value)
    {
        IsServer = _value;
    }

    /// <summary>
    /// Manages when connect button is clicked
    /// </summary>
    private void OnConnectClicked()
    {
        // Hide button
        Connect.gameObject.SetActive(false);

        // Subscribe to events
        if (IsServer)
            CS_NetworkManager.Instance.OnServerStartedEvent += OnServerStartedHandler;
        else
            CS_NetworkManager.Instance.OnClientConnectedEvent += OnClientConnectedHandler;

        // Send connection network manager
        CS_NetworkManager.Instance.InitConnection(IPAddress, IsServer);
    }

    /// <summary>
    /// Handles when server started
    /// </summary>
    private void OnServerStartedHandler()
    {
        if(null != ConnectionPanel)
            ConnectionPanel.SetActive(false);
    }

    /// <summary>
    /// Handles when client is connected
    /// </summary>
    private void OnClientConnectedHandler()
    {
        if (null != ConnectionPanel)
            ConnectionPanel.SetActive(false);
    }
    #endregion
}

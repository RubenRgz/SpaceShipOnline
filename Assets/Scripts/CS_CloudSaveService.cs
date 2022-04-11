using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Keys to obtain the string cloud keys
/// </summary>
public enum ECloudKeyData
{
    TotalScore = 0
}

public class CS_CloudSaveService : MonoBehaviour
{
    #region [Variables]
    // Basic singleton to have access to the Cloud Save Service
    public static CS_CloudSaveService Instance { get; private set; }
    public bool isOnline { get; private set; }
    public string PlayerID { get; private set; }

    Dictionary<ECloudKeyData, string> Keys = new Dictionary<ECloudKeyData, string>();

    public Action<ECloudKeyData, string> OnCloudDataRetrieved;
    public Action OnCloudServiceOnline;
    #endregion

    #region [Unity]
    private async void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }

        // Cloud Save needs to be initialized along with the other Unity Services that
        // it depends on (namely, Authentication), and then the user must sign in.
        // Also Unity Services Initialization works for Analytics
        await UnityServices.InitializeAsync();
        await SignInAnonymously();
    }

    // Start is called before the first frame update
    void Start()
    {
        CreateKeys();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    #endregion

    #region [Cloaud Save Methods]
    /// <summary>
    /// Saves persistence data in the cloud
    /// </summary>
    /// <param name="_key"></param>Type of cloud key to save
    /// <param name="_data"></param>Info to save
    public async void SaveCloudData(ECloudKeyData _key, string _data)
    {
        if (isOnline)
            await ForceSaveSingleData(Keys[_key], _data);
        else
            Debug.LogWarning("Cannot save data! Cloud Service is offline");
    }

    /// <summary>
    /// Obtains persistence data from tha cloud
    /// </summary>
    /// <param name="_key"></param>Key type to look for in the cloud
    public async void GetCloudData(ECloudKeyData _key)
    {
        if (isOnline)
        {
            var Data = await GetSingleData(Keys[_key]);
            OnCloudDataRetrieved?.Invoke(_key, Data);
        }
        else
        {
            Debug.LogWarning("Cannot load data! Cloud Service is offline");
        }
    }
    #endregion

    #region [Internal Logic]
    /// <summary>
    /// Creates dictionary of cloud keys
    /// </summary>
    private void CreateKeys()
    {
        Keys.Add(ECloudKeyData.TotalScore, "TotalScore");
    }
    #endregion

    #region [Authentication]
    /// <summary>
    /// Creates anonymous profile to connect to the game services
    /// </summary>
    /// <returns></returns>Anonymous sign in task
    private async Task SignInAnonymously()
    {
        AuthenticationService.Instance.SignedIn += () =>
        {
            PlayerID = AuthenticationService.Instance.PlayerId;
            //Debug.Log("Signed in as PlayerID: " + PlayerID);

            isOnline = true;
            OnCloudServiceOnline?.Invoke();
        };
        AuthenticationService.Instance.SignInFailed += s =>
        {
            // Take some action here...
            Debug.Log(s);
        };

        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
    #endregion

    #region [Tasks]
    /// <summary>
    /// Saves a single value data in the cloud
    /// </summary>
    /// <param name="_key"></param>Cloud key
    /// <param name="_value"></param>Value to save
    /// <returns></returns>
    private async Task ForceSaveSingleData(string _key, string _value)
    {
        try
        {
            Dictionary<string, object> oneElement = new Dictionary<string, object>();

            // It's a text input field, but let's see if you actually entered a number.
            if (Int32.TryParse(_value, out int wholeNumber))
            {
                oneElement.Add(_key, wholeNumber);
            }
            else if (Single.TryParse(_value, out float fractionalNumber))
            {
                oneElement.Add(_key, fractionalNumber);
            }
            else
            {
                oneElement.Add(_key, _value);
            }

            await SaveData.ForceSaveAsync(oneElement);

            Debug.Log($"Successfully saved {_key}:{_value}");
        }
        catch (CloudSaveValidationException e)
        {
            Debug.LogError(e);
        }
        catch (CloudSaveException e)
        {
            Debug.LogError(e);
        }
    }

    /// <summary>
    /// Obtains a single value from the cloud
    /// </summary>
    /// <param name="_key"></param>Cloud key
    /// <returns></returns>
    private async Task<string> GetSingleData(string _key)
    {
        try
        {
            Dictionary<string, string> savedData = await SaveData.LoadAsync(new HashSet<string> { _key });
            if (savedData.Count > 0)
                return savedData[_key];
            else
                Debug.LogWarning("Cannot fetch data from the key: " + _key);
        }
        catch (CloudSaveValidationException e)
        {
            Debug.LogError(e);
        }
        catch (CloudSaveException e)
        {
            Debug.LogError(e);
        }

        return null;
    }
    #endregion
}

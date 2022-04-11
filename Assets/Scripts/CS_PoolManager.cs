using System.Collections.Generic;
using UnityEngine;

public class CS_PoolManager : MonoBehaviour
{
    #region [Variables]
    List<GameObject> Pool = new List<GameObject>();
    #endregion

    #region [Functionality]
    /// <summary>
    /// Initializes a new pool manager
    /// </summary>
    /// <param name="_poolObj"></param>Game Object to instantiate
    /// <param name="_maxPool"></param>Maximum number of objects for this pool
    public void Init(GameObject _poolObj, int _maxPool)
    {
        for (int i = 0; i < _maxPool; i++)
        {
            GameObject Obj = Instantiate(_poolObj);
            Obj.SetActive(false);
            Pool.Add(Obj);
        }
    }

    /// <summary>
    /// Gets non active object form pool
    /// </summary>
    /// <returns></returns>Pool object
    public GameObject GetPoolObject()
    {
        for (int i = 0; i < Pool.Count; i++)
        {
            if(!Pool[i].activeSelf)
            {
                return Pool[i];
            }
        }
        return null;
    }

    /// <summary>
    /// Returns game object to the pool by reference
    /// </summary>
    /// <param name="_poolObj"></param>Pool object to return
    public void ReturnPoolObject(ref GameObject _poolObj)
    {
        for (int i = 0; i < Pool.Count; i++)
        {
            if (GameObject.ReferenceEquals(_poolObj, Pool[i]))
            {
                Pool[i].SetActive(false);
            }
        }
    }

    /// <summary>
    /// Cleans the pool manager
    /// </summary>
    public void Clean()
    {
        for (int i = 0; i < Pool.Count; i++)
        {
            Destroy(Pool[i]);
        }
        Pool.Clear();
    }
    #endregion
}

using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

/// <summary>
/// Package object
/// </summary>
public class Package
{
    public byte[] Data;
    public bool IsFree;
    public bool IsRead;

    /// <summary>
    /// Adds byte array data to this package
    /// </summary>
    /// <param name="_data"></param>
    public void AddData(ref byte[] _data)
    {
        _data.CopyTo(Data, 0);
    }

    /// <summary>
    /// Cleans all data stored
    /// </summary>
    public void ClearData()
    {
        Array.Clear(Data, 0, Data.Length);
    }
}

public class CS_PackagePoolManager : MonoBehaviour
{
    #region [Variables]
    int PackageSize = 0;

    List<Package> Packages = new List<Package>();
    #endregion

    #region [Unity]
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnDestroy()
    {
        for (int i = 0; i < Packages.Count; i++)
        {
            Array.Clear(Packages[i].Data, 0, Packages[i].Data.Length);
        }
        Packages.Clear();
    }
    #endregion

    #region [Functionality]
    /// <summary>
    /// Initializes a new package pool manager
    /// </summary>
    /// <param name="_size"></param>Package size
    /// <param name="_maxPool"></param>Maximum number of objects for this pool
    public void Init(int _size, int _maxPool)
    {
        PackageSize = _size;

        // for de paquetes
        for (int i = 0; i < _maxPool; i++)
        {
            Package Package = new Package();
            Package.Data = new byte[_size];
            Package.IsFree = true;
            Package.IsRead = false;

            Packages.Add(Package);
        }
    }

    /// <summary>
    /// Returns a free package in the pool
    /// </summary>
    /// <returns></returns>
    public Package GetPackage()
    {
        Package ReturnPackage;

        // If a package is free use it
        for (int i = 0; i < Packages.Count; i++)
        {
            if(Packages[i].IsFree)
            {
                ReturnPackage = Packages[i];
                return ReturnPackage;
            }
        }

        // Else create new one
        ReturnPackage = new Package();
        ReturnPackage.Data = new byte[PackageSize];
        ReturnPackage.IsFree = true;
        ReturnPackage.IsRead = false;

        Packages.Add(ReturnPackage);

        return ReturnPackage;
    }

    /// <summary>
    /// Retruns all the packages in the pool
    /// </summary>
    /// <returns></returns>Package list
    public List<Package> GetPackages()
    {
        return Packages;
    }

    /// <summary>
    /// Updates packages state
    /// </summary>
    public void UpdatePackages()
    {
        foreach (Package CurrentPackage in Packages)
        {
            if(CurrentPackage.IsRead)
            {
                CurrentPackage.ClearData();

                CurrentPackage.IsFree = true;
                CurrentPackage.IsRead = false;
            }
        }
    }
    #endregion
}

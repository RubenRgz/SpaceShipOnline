using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

public class CS_Bubble : MonoBehaviour
{
    #region [Variables]
    public uint ID { get; set; }

    int CurrentWaypoint = 0;
    bool NeedUpdate = false;
    float DeltaWaypointPosition = 0.0f;
    float Speed = 0.0f;
    bool IsWaypointChange = true;

    List<Vector2> Path = new List<Vector2>();
    Vector3 LastPosition = new Vector3();

    public Action<CS_Bubble> OnFinishPathEvent;
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

    private void FixedUpdate()
    {
        if(NeedUpdate)
        {
            if (IsWaypointChange)
            {
                LastPosition = transform.position;
                CurrentWaypoint++;
                IsWaypointChange = false;
            }

            if (CurrentWaypoint < Path.Count)
            {
                DeltaWaypointPosition += Time.fixedDeltaTime;

                Vector3 NextPosition = Path[CurrentWaypoint];
                float Distance = (NextPosition - LastPosition).magnitude;
                float WaypointTime = Distance / (Speed * Time.fixedDeltaTime);

                if (DeltaWaypointPosition < WaypointTime)
                {
                    float Time = DeltaWaypointPosition / WaypointTime;
                    transform.position = Vector3.Lerp(LastPosition, NextPosition, Time);
                }
                else
                {
                    IsWaypointChange = true;
                    DeltaWaypointPosition -= WaypointTime;
                }
            }
            else
            {
                // Finish the path
                NeedUpdate = false;
                OnFinishPathEvent?.Invoke(this);
            }
        }
    }
    #endregion

    #region[Gameplay]
    /// <summary>
    /// Initializes bubble with server data
    /// </summary>
    /// <param name="_path"></param>Chosen path to follow
    /// <param name="_speed"></param>Bubble movement speed
    public void Init(List<Vector2> _path, float _speed)
    {
        Path = _path;
        CurrentWaypoint = 0;
        transform.position = _path[CurrentWaypoint];
        LastPosition = transform.position;
        Speed = _speed;
        NeedUpdate = true;
        IsWaypointChange = true;
    }
    #endregion

    #region[Collisions]
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if(CS_NetworkManager.Instance.IsServer)
        {
            // Check with who we collide
            CS_Bullet BulletComponent = collision.gameObject.GetComponent<CS_Bullet>();
            CS_ShipPlayer ShipComponent = collision.gameObject.GetComponent<CS_ShipPlayer>();
            if (BulletComponent != null)
            {
                // Notify collision
                CS_NetworkManager.Instance.SendBulletCollisionMessage(BulletComponent.PlayerID, BulletComponent.ID, ID);
            }
            else if (ShipComponent != null)
            {
                // Notify collision
                CS_NetworkManager.Instance.SendShipCollisionMessage(ShipComponent.GetPlayerID(), ID);
            }
        }
    }
    #endregion
}

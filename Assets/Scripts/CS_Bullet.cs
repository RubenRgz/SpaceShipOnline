using System;
using UnityEngine;

public class CS_Bullet : MonoBehaviour
{
    #region[Variables]
    public int PlayerID { get; set; }
    public uint ID { get; set; }

    float MovementSpeed = 20.0f;
    bool IsShooted = false;
    float LifeTime = 3.0f; // Seconds
    float LifeTimeCounter = 0.0f;

    public Action<CS_Bullet> OnLifeTimeExpiredEvent;
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
        if(IsShooted)
        {
            LifeTimeCounter += Time.fixedDeltaTime;
            if (LifeTimeCounter > LifeTime)
                OnLifeTimeExpiredEvent?.Invoke(this);

            transform.position = new Vector3(transform.position.x, transform.position.y + (MovementSpeed * Time.fixedDeltaTime));
        }
    }

    private void OnEnable()
    {
        LifeTimeCounter = 0.0f;
        IsShooted = true;
    }

    private void OnDisable()
    {
        IsShooted = false;
    }
    #endregion
}

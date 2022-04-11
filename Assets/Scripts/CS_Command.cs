using UnityEngine;

// Game pattern to manages user inputs
public abstract class CS_Command
{
    public abstract void Execute(ref GameObject _gameObject);
}

public class CS_LeftCommand : CS_Command
{
    public override void Execute(ref GameObject _gameObject)
    {
        CS_NetworkManager.Instance.AddClientInputToQueue(EInputType.Left);
        CS_ShipPlayer Player = _gameObject.GetComponent<CS_ShipPlayer>();
        if (Player != null)
            Player.Move((int)EInputType.Left);
    }
}

public class CS_RightCommand : CS_Command
{
    public override void Execute(ref GameObject _gameObject)
    {
        CS_NetworkManager.Instance.AddClientInputToQueue(EInputType.Right);
        CS_ShipPlayer Player = _gameObject.GetComponent<CS_ShipPlayer>();
        if (Player != null)
            Player.Move((int)EInputType.Right);
    }
}

public class CS_UpCommand : CS_Command
{
    public override void Execute(ref GameObject _gameObject)
    {
        CS_NetworkManager.Instance.AddClientInputToQueue(EInputType.Up);
        CS_ShipPlayer Player = _gameObject.GetComponent<CS_ShipPlayer>();
        if (Player != null)
            Player.Move((int)EInputType.Up);
    }
}

public class CS_DownCommand : CS_Command
{
    public override void Execute(ref GameObject _gameObject)
    {
        CS_NetworkManager.Instance.AddClientInputToQueue(EInputType.Down);
        CS_ShipPlayer Player = _gameObject.GetComponent<CS_ShipPlayer>();
        if (Player != null)
            Player.Move((int)EInputType.Down);
    }
}

public class CS_ShootCommand : CS_Command
{
    public override void Execute(ref GameObject _gameObject)
    {
        CS_NetworkManager.Instance.AddClientInputToQueue(EInputType.Shoot);
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reference points in the editor
/// </summary>
[System.Serializable]
public struct SPoints
{
    public Transform p1;
    public Transform p2;
    public Transform p3;
    public Transform p4;
}

[ExecuteInEditMode]
public class CS_EditorBezierPath : MonoBehaviour
{
    #region [Variables]
    public int CurveSamples = 20;
    public bool Draw = false;

    public List<SPoints> Points = new List<SPoints>();
    CS_BezierPath BezierPath = null;
    List<Vector2> Path = new List<Vector2>();
    #endregion

    #region [Unity]
    // Start is called before the first frame update
    void Start()
    {
        // Create instance of the bezier path and attach it to this object
        BezierPath = gameObject.AddComponent<CS_BezierPath>();
    }

    // Update is called once per frame
    void Update()
    {
        if(Points.Count > 0)
        {
            Path.Clear();
            BezierPath.Clear();

            // Create curves
            for (int i = 0; i < Points.Count; i++)
            {
                if (Points[i].p1 != null && Points[i].p2 != null &&
                    Points[i].p3 != null && Points[i].p4 != null)
                {
                    SBezierCurve curve = new SBezierCurve();
                    curve.p0 = Points[i].p1.position;
                    curve.p1 = Points[i].p2.position;
                    curve.p2 = Points[i].p3.position;
                    curve.p3 = Points[i].p4.position;

                    BezierPath.AddCurve(curve, CurveSamples);
                }
            }

            BezierPath.Sample(ref Path);

            // Render Path
            if (Draw)
            {
                for (int i = 0; i < Path.Count - 1; i++)
                {
                    Debug.DrawLine(new Vector3(Path[i].x, Path[i].y), 
                                   new Vector3(Path[i + 1].x, Path[i + 1].y), 
                                   Color.red);
                }
            }
        }
    }
    #endregion
}

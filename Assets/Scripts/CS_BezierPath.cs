using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bezier curve data
/// </summary>
public struct SBezierCurve
{
    public Vector2 p0;
    public Vector2 p1;
    public Vector2 p2;
    public Vector2 p3;

    public  Vector2 CalculateCurvePoint(float _time)
    {
        float tt = _time * _time;
        float ttt = tt * _time;
        float u = 1.0f - _time;
        float uu = u * u;
        float uuu = uu * u;

        // Cubic curve calculation (4 points)
        Vector2 point = (uuu * p0) + ((3 * uu) * _time * p1) + ((3 * u) * tt * p2) + (ttt * p3);
        return point;
    }
}

public class CS_BezierPath : MonoBehaviour
{
    #region [Variables]
    public List<SBezierCurve> Curves = new List<SBezierCurve>();
    List<int> Samples = new List<int>();
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
    #endregion

    #region [Functionality]
    /// <summary>
    /// Adds a new bezier cureve to the list
    /// </summary>
    /// <param name="_curve"></param>Bezier curve
    /// <param name="_samples"></param>Number of samples to split the curve
    public void AddCurve(SBezierCurve _curve, int _samples)
    {
        Curves.Add(_curve);
        Samples.Add(_samples);
    }

    /// <summary>
    /// Samples the current curves
    /// </summary>
    /// <param name="_samplePath"></param>
    public void Sample(ref List<Vector2> _samplePath)
    {
        for (int i = 0; i < Curves.Count; i++)
        {
            for (float t = 0.0f; t <= 1.0f; t += (1.0f / Samples[i]))
            {
                _samplePath.Add(Curves[i].CalculateCurvePoint(t));
            }
        }
    }

    /// <summary>
    /// Clean all the lists
    /// </summary>
    public void Clear()
    {
        Curves.Clear();
        Samples.Clear();
    }
    #endregion
}

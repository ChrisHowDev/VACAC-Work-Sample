using UnityEngine;

public class ConveyorManager : MonoBehaviour
{
    public static ConveyorManager Instance;

    [Header("Global Conveyor Settings")]
    public float speed = 5f;
    public float friction = 10f;

    [Header("Speed control")]
    [Tooltip("Upper limit of the speed slider in the placer panel")]
    public float maxSpeed = 15f;

    [Header("Arrow visuals")]
    [Tooltip("Multiplier on how fast the arrows scroll, on top of following the belt speed. Tune it once (slider in the " +
             "placer panel while playing) until the arrows travel at the same speed as the items, then type the value here.")]
    public float arrowSpeedScale = 4.2f;

    /// <summary>The speed the scene started with. The arrows' scroll speed is scaled by speed / referenceSpeed.</summary>
    public float referenceSpeed { get; private set; } = 1f;

    /// <summary>1 at the starting speed, 2 at double speed, 0 when stopped.</summary>
    public float SpeedRatio => speed / referenceSpeed;

    /// <summary>What the arrows' Direction X is multiplied by: follows the belt speed, times the arrow match setting.</summary>
    public float ArrowFactor => SpeedRatio * arrowSpeedScale;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            referenceSpeed = Mathf.Max(0.0001f, speed);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
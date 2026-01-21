using UnityEngine;

public class ArenaAnchor : MonoBehaviour
{
    public static ArenaAnchor Instance;
    public Transform ArenaRoot;

    void Awake()
    {
        Instance = this;
    }

    public void SetAnchor(Transform target)
    {
        ArenaRoot = target;
        Debug.Log("Arena Anchor Set: " + target.name);
    }
}

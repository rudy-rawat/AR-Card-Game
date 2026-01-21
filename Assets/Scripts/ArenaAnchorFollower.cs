using UnityEngine;

public class ArenaAnchorFollower : MonoBehaviour
{
    void Update()
    {
        if (ArenaAnchor.Instance.ArenaRoot != null)
        {
            transform.SetPositionAndRotation(
                ArenaAnchor.Instance.ArenaRoot.position,
                ArenaAnchor.Instance.ArenaRoot.rotation
            );
        }
    }
}

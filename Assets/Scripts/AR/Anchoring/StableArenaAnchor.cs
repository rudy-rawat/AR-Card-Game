using System;
using UnityEngine;

public class StableArenaAnchor : MonoBehaviour
{
    public Transform trackedTarget;
    public float positionLerpSpeed = 5f;
    public float rotationLerpSpeed = 5f;

    private bool anchorLocked = false;

    public void LockAnchor(Transform target)
    {
        trackedTarget = target;
        anchorLocked = true;
    }

    void Update()
    {
        if (!anchorLocked || trackedTarget == null)
        {
            return;
        }
        
        transform.position = Vector3.Lerp(
            transform.position,
            trackedTarget.position,
            Time.deltaTime * positionLerpSpeed
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            trackedTarget.rotation,
            Time.deltaTime * rotationLerpSpeed
        );
    }
}

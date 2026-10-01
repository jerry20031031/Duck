using UnityEngine;

/// <summary>
/// Small collision-aware motor for Fusion state-authority ducks.
///
/// Fusion's NetworkTransform replicates an object's transform; it does not
/// integrate Rigidbody velocity.  This helper therefore advances the
/// state-authority body directly during FixedUpdateNetwork while using its
/// colliders to stop at walls, props, players, and the map ground.
/// </summary>
public static class DuckKinematicMotor
{
    private const float CollisionSkin = 0.015f;

    /// <summary>
    /// Moves the body as far as the requested displacement permits. Returns
    /// true when a collider blocked the requested path.
    /// </summary>
    public static bool Move(Rigidbody body, Vector3 displacement)
    {
        if (body == null || displacement.sqrMagnitude <= 0.0000001f)
        {
            return false;
        }

        float requestedDistance = displacement.magnitude;
        Vector3 direction = displacement / requestedDistance;
        bool blocked = body.SweepTest(
            direction,
            out RaycastHit hit,
            requestedDistance + CollisionSkin,
            QueryTriggerInteraction.Ignore);

        // A capsule that is resting a fraction inside the terrain can report
        // the ground as an immediate hit for a *horizontal* sweep. That made
        // AI ducks believe every road was a wall. Keep vertical floor checks
        // intact, but ignore only this sideways floor-contact false positive.
        if (blocked
            && Mathf.Abs(direction.y) < 0.1f
            && Mathf.Abs(hit.normal.y) > 0.65f)
        {
            blocked = false;
        }

        float allowedDistance = blocked
            ? Mathf.Max(0f, hit.distance - CollisionSkin)
            : requestedDistance;
        if (allowedDistance > 0f)
        {
            body.position += direction * allowedDistance;
            // The next tick may cast from this new pose. The project has
            // Auto Sync Transforms off, so publish it before that query.
            Physics.SyncTransforms();
        }

        return blocked;
    }
}

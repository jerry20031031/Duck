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
        return Move(body, displacement, out _, false);
    }

    /// <summary>
    /// Moves the body while also exposing the blocking surface. AI callers use
    /// that surface to distinguish a small, jumpable prop from a wall that
    /// needs a new route. A default hit means the move was clear.
    /// </summary>
    public static bool Move(
        Rigidbody body,
        Vector3 displacement,
        out RaycastHit blockingHit,
        bool ignoreDynamicBodies = false)
    {
        blockingHit = default;
        if (body == null || displacement.sqrMagnitude <= 0.0000001f)
        {
            return false;
        }

        float requestedDistance = displacement.magnitude;
        Vector3 direction = displacement / requestedDistance;
        bool blocked = TryGetBlockingHit(
            body,
            direction,
            requestedDistance + CollisionSkin,
            ignoreDynamicBodies,
            out RaycastHit hit);

        if (blocked)
        {
            blockingHit = hit;
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

    /// <summary>
    /// AI may move through other moving network objects while it follows the
    /// static route grid.  Treating another duck or a pickup as a wall causes
    /// an entire spawn group to freeze when its members begin close together
    /// or converge on the same wand.  Static geometry is still returned as a
    /// blocking hit and therefore remains collision-safe.
    /// </summary>
    public static bool TryGetBlockingHit(
        Rigidbody body,
        Vector3 direction,
        float maxDistance,
        bool ignoreDynamicBodies,
        out RaycastHit blockingHit)
    {
        blockingHit = default;
        if (body == null || direction.sqrMagnitude < 0.000001f || maxDistance <= 0f) return false;
        direction.Normalize();
        RaycastHit[] hits = body.SweepTestAll(direction, maxDistance, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        for (int index = 0; index < hits.Length; index++)
        {
            RaycastHit hit = hits[index];
            Collider collider = hit.collider;
            if (collider == null
                || collider.attachedRigidbody == body
                || (ignoreDynamicBodies && (collider.attachedRigidbody != null
                    || collider.GetComponentInParent<Fusion.NetworkObject>() != null)))
            {
                continue;
            }

            // Filter each floor hit, then keep looking for a wall behind it.
            // Discarding just the nearest result hid real blockers and made
            // recovery sweeps disagree with normal horizontal movement.
            if (Mathf.Abs(direction.y) < 0.1f && Mathf.Abs(hit.normal.y) > 0.65f) continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                blockingHit = hit;
            }
        }

        return nearestDistance < float.PositiveInfinity;
    }
}

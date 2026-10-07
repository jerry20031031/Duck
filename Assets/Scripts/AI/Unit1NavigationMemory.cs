using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded, expiring failures shared by CPU and Grey Shadow decisions.</summary>
public sealed class Unit1NavigationMemory
{
    private const int Capacity = 8;
    private readonly List<Goal> goals = new();

    private readonly struct Goal
    {
        public readonly Vector3 Position;
        public readonly float RetryAt;
        public Goal(Vector3 position, float retryAt) { Position = position; RetryAt = retryAt; }
    }

    public void Remember(Vector3 position, float now, float duration = 5f)
    {
        for (int i = goals.Count - 1; i >= 0; i--)
            if (now >= goals[i].RetryAt || Near(position, goals[i].Position)) goals.RemoveAt(i);
        if (goals.Count >= Capacity) goals.RemoveAt(0);
        goals.Add(new Goal(position, now + duration));
    }

    public bool IsUnreachable(Vector3 position, float now)
    {
        for (int i = goals.Count - 1; i >= 0; i--)
        {
            if (now >= goals[i].RetryAt) { goals.RemoveAt(i); continue; }
            if (Near(position, goals[i].Position)) return true;
        }
        return false;
    }

    private static bool Near(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return (a - b).sqrMagnitude < 1.5f * 1.5f;
    }
}

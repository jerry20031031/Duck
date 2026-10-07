using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single source of truth for UNIT1's match factions.  A faction is a
/// gameplay relationship, not a duck skin: only the three valid match teams
/// can be allies or enemies, while 0 remains neutral/unassigned.
/// </summary>
public static class FactionTeam
{
    public const int Neutral = 0;
    public const int Red = 1;
    public const int Blue = 2;
    public const int Green = 3;
    public const int Count = 3;

    private static readonly Color[] Colors =
    {
        new Color(1f, 0.28f, 0.28f),
        new Color(0.25f, 0.55f, 1f),
        new Color(0.25f, 1f, 0.45f)
    };

    private static readonly string[] Names = { "紅", "藍", "綠" };

    public static bool IsAssigned(int factionIndex)
    {
        return factionIndex >= Red && factionIndex <= Count;
    }

    public static int Normalize(int factionIndex)
    {
        return IsAssigned(factionIndex) ? factionIndex : Neutral;
    }

    public static string GetName(int factionIndex)
    {
        return IsAssigned(factionIndex) ? Names[factionIndex - 1] : "未分配";
    }

    public static Color GetColor(int factionIndex)
    {
        return IsAssigned(factionIndex) ? Colors[factionIndex - 1] : Color.white;
    }

    /// <summary>
    /// Returns a deterministic permutation of the three match factions. The
    /// director uses this for fixed world objectives, so their locations can
    /// remain editor-authored while their colours change every match.
    /// </summary>
    public static int[] CreateShuffledFactionOrder(int seed)
    {
        return CreateShuffledFactionOrder(new System.Random(seed));
    }

    /// <summary>Neutral or not-yet-assigned ducks are never considered allied.</summary>
    public static bool AreAllies(int firstFaction, int secondFaction)
    {
        return IsAssigned(firstFaction) && firstFaction == secondFaction;
    }

    /// <summary>Only two assigned, different teams are valid PvP opponents.</summary>
    public static bool AreEnemies(int firstFaction, int secondFaction)
    {
        return IsAssigned(firstFaction)
            && IsAssigned(secondFaction)
            && firstFaction != secondFaction;
    }

    /// <summary>
    /// Builds a deterministic, balanced assignment for the sorted human list
    /// followed by the sorted CPU list.  The director supplies its synced seed
    /// so every authority reaches the same team layout.  With one human, the
    /// first CPU is deliberately placed on that human's team.
    /// </summary>
    public static int[] CreateBalancedAssignments(int humanCount, int cpuCount, int seed)
    {
        humanCount = Mathf.Max(0, humanCount);
        cpuCount = Mathf.Max(0, cpuCount);
        int participantCount = humanCount + cpuCount;
        int[] assignments = new int[participantCount];
        if (participantCount == 0)
        {
            return assignments;
        }

        System.Random random = new(seed);
        int[] factionOrder = CreateShuffledFactionOrder(random);
        int[] population = new int[Count + 1];

        // Give humans the first places in a shuffled round-robin order. This
        // keeps a four-to-six-player match at 2/1/1, 2/2/1, or 2/2/2.
        for (int humanIndex = 0; humanIndex < humanCount; humanIndex++)
        {
            int faction = factionOrder[humanIndex % Count];
            assignments[humanIndex] = faction;
            population[faction]++;
        }

        for (int cpuIndex = 0; cpuIndex < cpuCount; cpuIndex++)
        {
            int faction;
            if (humanCount == 1 && cpuIndex == 0)
            {
                // A solo player always starts with one allied CPU, making the
                // intended 2 vs 1 vs 1 opening possible.
                faction = assignments[0];
            }
            else
            {
                faction = ChooseLeastPopulatedFaction(population, random);
            }

            assignments[humanCount + cpuIndex] = faction;
            population[faction]++;
        }

        return assignments;
    }

    private static int ChooseLeastPopulatedFaction(IReadOnlyList<int> population, System.Random random)
    {
        int minimum = int.MaxValue;
        List<int> candidates = new(Count);
        for (int faction = Red; faction <= Count; faction++)
        {
            int currentPopulation = population[faction];
            if (currentPopulation < minimum)
            {
                minimum = currentPopulation;
                candidates.Clear();
                candidates.Add(faction);
            }
            else if (currentPopulation == minimum)
            {
                candidates.Add(faction);
            }
        }

        return candidates[random.Next(candidates.Count)];
    }

    private static int[] CreateShuffledFactionOrder(System.Random random)
    {
        int[] factionOrder = { Red, Blue, Green };
        Shuffle(factionOrder, random);
        return factionOrder;
    }

    private static void Shuffle(int[] values, System.Random random)
    {
        for (int index = values.Length - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}

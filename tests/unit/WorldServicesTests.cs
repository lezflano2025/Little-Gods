/// <summary>
/// GdUnit4 tests for WorldServices. Covers TimeOfDay wrapping, AgentsNear
/// boundary inclusivity, exclusion of out-of-range agents, and determinism.
/// </summary>

using System.Collections.Generic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using LittleGods.Agent;
using LittleGods.World;

namespace LittleGods.Tests;

[TestSuite]
public class WorldServicesTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static AgentState MakeAgent(int id, Vector3 position)
    {
        var xform = Transform3D.Identity;
        xform.Origin = position;
        return new AgentState(id, speciesId: 0, xform, new DeterministicRng((ulong)id + 1UL));
    }

    private static WorldServices MakeServices(double dayLength = 300.0)
        => new WorldServices(FlatGround.Zero, dayLength);

    // ──────────────────────────────────────────────────────────────────────────
    // 1. TimeOfDay is in [0, 1)
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void TimeOfDay_IsInZeroToOne_AtStart()
    {
        var ws = MakeServices();
        AssertBool(ws.TimeOfDay >= 0.0).IsTrue();
        AssertBool(ws.TimeOfDay <  1.0).IsTrue();
    }

    [TestCase]
    public void TimeOfDay_IsInZeroToOne_OverManyAdvances()
    {
        var ws = MakeServices(dayLength: 60.0);
        for (int i = 0; i < 200; i++)
        {
            ws.Advance(1.3);
            AssertBool(ws.TimeOfDay >= 0.0).IsTrue();
            AssertBool(ws.TimeOfDay <  1.0).IsTrue();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. TimeOfDay wraps across a day boundary
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void TimeOfDay_WrapsAcrossDayBoundary()
    {
        const double dayLen = 100.0;
        var ws = MakeServices(dayLength: dayLen);

        ws.ElapsedSeconds = 99.9;
        double near_end = ws.TimeOfDay;

        ws.Advance(0.2);                  // now at 100.1 — past one full day
        double past_end = ws.TimeOfDay;

        // near_end should be close to 1; past_end should be close to 0
        AssertBool(near_end > 0.99).IsTrue();
        AssertBool(past_end < 0.01).IsTrue();
        AssertBool(past_end < near_end).IsTrue();
    }

    [TestCase]
    public void TimeOfDay_ExactlyOneDayBoundary_IsZero()
    {
        const double dayLen = 100.0;
        var ws = MakeServices(dayLength: dayLen);
        ws.ElapsedSeconds = dayLen;

        AssertFloat((float)ws.TimeOfDay).IsEqualApprox(0f, 1e-9f);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. AgentsNear — boundary inclusive
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void AgentsNear_IncludesAgentExactlyOnBoundary()
    {
        var ws = MakeServices();
        var centre = new Vector3(0f, 0f, 0f);
        const float radius = 5f;

        // Place one agent exactly at the radius boundary
        var agentOnBoundary = MakeAgent(1, new Vector3(radius, 0f, 0f));
        ws.SetAgents(new List<AgentState> { agentOnBoundary });

        IReadOnlyList<AgentState> result = ws.AgentsNear(centre, radius);
        AssertInt(result.Count).IsEqual(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. AgentsNear — excludes agents outside radius
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void AgentsNear_ExcludesAgentOutsideRadius()
    {
        var ws = MakeServices();
        var centre = new Vector3(0f, 0f, 0f);
        const float radius = 5f;

        var outside = MakeAgent(1, new Vector3(5.01f, 0f, 0f));
        ws.SetAgents(new List<AgentState> { outside });

        IReadOnlyList<AgentState> result = ws.AgentsNear(centre, radius);
        AssertInt(result.Count).IsEqual(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. AgentsNear — returns exactly the agents within radius
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void AgentsNear_ReturnsCorrectSubset()
    {
        var ws = MakeServices();
        var centre = new Vector3(10f, 0f, 10f);
        const float radius = 8f;

        // Agents at known distances from centre
        var inside1 = MakeAgent(1, new Vector3(10f, 0f, 15f));  // dist = 5  → inside
        var inside2 = MakeAgent(2, new Vector3(14f, 0f, 10f));  // dist = 4  → inside
        var outside = MakeAgent(3, new Vector3(0f,  0f, 10f));  // dist = 10 → outside
        var atEdge  = MakeAgent(4, new Vector3(10f, 0f, 18f));  // dist = 8  → on boundary, included

        ws.SetAgents(new List<AgentState> { inside1, inside2, outside, atEdge });

        IReadOnlyList<AgentState> result = ws.AgentsNear(centre, radius);
        AssertInt(result.Count).IsEqual(3);

        // Verify the right IDs are present
        bool hasId1 = false, hasId2 = false, hasId4 = false;
        foreach (var a in result)
        {
            if (a.Id == 1) hasId1 = true;
            if (a.Id == 2) hasId2 = true;
            if (a.Id == 4) hasId4 = true;
        }
        AssertBool(hasId1).IsTrue();
        AssertBool(hasId2).IsTrue();
        AssertBool(hasId4).IsTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. AgentsNear — empty list returns no agents
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void AgentsNear_EmptyList_ReturnsEmpty()
    {
        var ws = MakeServices();
        ws.SetAgents(new List<AgentState>());
        AssertInt(ws.AgentsNear(Vector3.Zero, 100f).Count).IsEqual(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Ground property returns the injected sampler
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Ground_ReturnsInjectedSampler()
    {
        var flat = new FlatGround(3f);
        var ws   = new WorldServices(flat);
        AssertFloat(ws.Ground.HeightAt(0f, 0f)).IsEqualApprox(3f, 1e-6f);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Determinism: same elapsed time → same TimeOfDay
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void TimeOfDay_Deterministic_SameElapsed_SameResult()
    {
        var wsA = MakeServices(dayLength: 300.0);
        var wsB = MakeServices(dayLength: 300.0);

        wsA.ElapsedSeconds = 1234.5;
        wsB.ElapsedSeconds = 1234.5;

        AssertFloat((float)wsA.TimeOfDay).IsEqual((float)wsB.TimeOfDay);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Advance accumulates correctly
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Advance_AccumulatesElapsedSeconds()
    {
        var ws = MakeServices();
        ws.Advance(10.0);
        ws.Advance(5.5);
        AssertFloat((float)ws.ElapsedSeconds).IsEqualApprox(15.5f, 1e-6f);
    }
}

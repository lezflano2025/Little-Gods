/// <summary>
/// M4 P0 — BT runtime unit tests (ADR-0005, m4-contract.md §Agent A).
/// Covers every composite/decorator/leaf transition and an assembled-tree integration test.
/// </summary>

using System.Collections.Generic;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using LittleGods.Agent;
using LittleGods.World;

namespace LittleGods.Tests;

[TestSuite]
public class BtRuntimeTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal test stub: returns a fixed BtStatus. Counts how many times it is
    /// ticked so callers can assert early-out behaviour.
    /// </summary>
    private sealed class Stub : IBtTask
    {
        private readonly BtStatus _result;
        public int TickCount { get; private set; }

        public Stub(BtStatus result) => _result = result;

        public BtStatus Tick(in BtContext ctx)
        {
            TickCount++;
            return _result;
        }
    }

    /// <summary>
    /// Minimal IWorldServices stub that satisfies the interface with safe defaults.
    /// Ground = FlatGround.Zero; AgentsNear returns empty list.
    /// </summary>
    private sealed class StubWorld : IWorldServices
    {
        public IGroundSampler Ground { get; } = FlatGround.Zero;
        public double ElapsedSeconds => 0.0;
        public double TimeOfDay => 0.0;
        public IReadOnlyList<AgentState> AgentsNear(Vector3 position, float radius)
            => System.Array.Empty<AgentState>();
    }

    /// <summary>Builds a minimal BtContext usable in all tests.</summary>
    private static BtContext MakeCtx()
    {
        var rng = new DeterministicRng(42UL);
        var agent = new AgentState(1, 0, Transform3D.Identity, rng);
        var bb = new Blackboard();
        var world = new StubWorld();
        return new BtContext(agent, bb, world, 0.016);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Sequence
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Sequence_EmptyChildren_ReturnsSuccess()
    {
        IBtTask seq = Bt.Sequence();
        AssertThat((int)seq.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Sequence_AllSuccess_ReturnsSuccess()
    {
        IBtTask seq = Bt.Sequence(
            new Stub(BtStatus.Success),
            new Stub(BtStatus.Success),
            new Stub(BtStatus.Success));
        AssertThat((int)seq.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Sequence_FirstChildFailure_ReturnsFailureAndStops()
    {
        var fail = new Stub(BtStatus.Failure);
        var after = new Stub(BtStatus.Success);
        IBtTask seq = Bt.Sequence(fail, after);
        BtStatus result = seq.Tick(MakeCtx());
        AssertThat((int)result).IsEqual((int)BtStatus.Failure);
        AssertThat(fail.TickCount).IsEqual(1);
        AssertThat(after.TickCount).IsEqual(0);
    }

    [TestCase]
    public void Sequence_MiddleChildFailure_StopsAtFailingChild()
    {
        var s1 = new Stub(BtStatus.Success);
        var fail = new Stub(BtStatus.Failure);
        var s2 = new Stub(BtStatus.Success);
        IBtTask seq = Bt.Sequence(s1, fail, s2);
        BtStatus result = seq.Tick(MakeCtx());
        AssertThat((int)result).IsEqual((int)BtStatus.Failure);
        AssertThat(s1.TickCount).IsEqual(1);
        AssertThat(fail.TickCount).IsEqual(1);
        AssertThat(s2.TickCount).IsEqual(0);
    }

    [TestCase]
    public void Sequence_FirstChildRunning_ReturnsRunningAndStops()
    {
        var run = new Stub(BtStatus.Running);
        var after = new Stub(BtStatus.Success);
        IBtTask seq = Bt.Sequence(run, after);
        BtStatus result = seq.Tick(MakeCtx());
        AssertThat((int)result).IsEqual((int)BtStatus.Running);
        AssertThat(run.TickCount).IsEqual(1);
        AssertThat(after.TickCount).IsEqual(0);
    }

    [TestCase]
    public void Sequence_SuccessThenRunning_StopsAtRunning()
    {
        var s = new Stub(BtStatus.Success);
        var run = new Stub(BtStatus.Running);
        var after = new Stub(BtStatus.Failure);
        IBtTask seq = Bt.Sequence(s, run, after);
        BtStatus result = seq.Tick(MakeCtx());
        AssertThat((int)result).IsEqual((int)BtStatus.Running);
        AssertThat(s.TickCount).IsEqual(1);
        AssertThat(run.TickCount).IsEqual(1);
        AssertThat(after.TickCount).IsEqual(0);
    }

    [TestCase]
    public void Sequence_Memoryless_ReticksFromFirstChildEachTick()
    {
        // Even after a Running, next tick must re-evaluate from child 0.
        var s1 = new Stub(BtStatus.Success);
        var run = new Stub(BtStatus.Running);
        IBtTask seq = Bt.Sequence(s1, run);
        seq.Tick(MakeCtx()); // tick 1: s1=1, run=1
        seq.Tick(MakeCtx()); // tick 2: s1=2, run=2 (memoryless)
        AssertThat(s1.TickCount).IsEqual(2);
        AssertThat(run.TickCount).IsEqual(2);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Selector
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Selector_EmptyChildren_ReturnsFailure()
    {
        IBtTask sel = Bt.Selector();
        AssertThat((int)sel.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void Selector_AllFailure_ReturnsFailure()
    {
        IBtTask sel = Bt.Selector(
            new Stub(BtStatus.Failure),
            new Stub(BtStatus.Failure));
        AssertThat((int)sel.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void Selector_FirstChildSuccess_ReturnsSuccessAndStops()
    {
        var succ = new Stub(BtStatus.Success);
        var after = new Stub(BtStatus.Failure);
        IBtTask sel = Bt.Selector(succ, after);
        BtStatus result = sel.Tick(MakeCtx());
        AssertThat((int)result).IsEqual((int)BtStatus.Success);
        AssertThat(succ.TickCount).IsEqual(1);
        AssertThat(after.TickCount).IsEqual(0);
    }

    [TestCase]
    public void Selector_FailureThenSuccess_StopsAtSuccess()
    {
        var fail = new Stub(BtStatus.Failure);
        var succ = new Stub(BtStatus.Success);
        var after = new Stub(BtStatus.Failure);
        IBtTask sel = Bt.Selector(fail, succ, after);
        BtStatus result = sel.Tick(MakeCtx());
        AssertThat((int)result).IsEqual((int)BtStatus.Success);
        AssertThat(fail.TickCount).IsEqual(1);
        AssertThat(succ.TickCount).IsEqual(1);
        AssertThat(after.TickCount).IsEqual(0);
    }

    [TestCase]
    public void Selector_FirstChildRunning_ReturnsRunningAndStops()
    {
        var run = new Stub(BtStatus.Running);
        var after = new Stub(BtStatus.Failure);
        IBtTask sel = Bt.Selector(run, after);
        BtStatus result = sel.Tick(MakeCtx());
        AssertThat((int)result).IsEqual((int)BtStatus.Running);
        AssertThat(run.TickCount).IsEqual(1);
        AssertThat(after.TickCount).IsEqual(0);
    }

    [TestCase]
    public void Selector_FailureThenRunning_StopsAtRunning()
    {
        var fail = new Stub(BtStatus.Failure);
        var run = new Stub(BtStatus.Running);
        var after = new Stub(BtStatus.Success);
        IBtTask sel = Bt.Selector(fail, run, after);
        BtStatus result = sel.Tick(MakeCtx());
        AssertThat((int)result).IsEqual((int)BtStatus.Running);
        AssertThat(fail.TickCount).IsEqual(1);
        AssertThat(run.TickCount).IsEqual(1);
        AssertThat(after.TickCount).IsEqual(0);
    }

    [TestCase]
    public void Selector_Memoryless_ReticksFromFirstChildEachTick()
    {
        var fail = new Stub(BtStatus.Failure);
        var run = new Stub(BtStatus.Running);
        IBtTask sel = Bt.Selector(fail, run);
        sel.Tick(MakeCtx());
        sel.Tick(MakeCtx());
        AssertThat(fail.TickCount).IsEqual(2);
        AssertThat(run.TickCount).IsEqual(2);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Parallel — RequireAll
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Parallel_RequireAll_EmptyChildren_ReturnsSuccess()
    {
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireAll);
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Parallel_RequireAll_AllSuccess_ReturnsSuccess()
    {
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireAll,
            new Stub(BtStatus.Success),
            new Stub(BtStatus.Success));
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Parallel_RequireAll_AnyFailure_ReturnsFailure()
    {
        var s = new Stub(BtStatus.Success);
        var fail = new Stub(BtStatus.Failure);
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireAll, s, fail);
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void Parallel_RequireAll_AnyRunning_NoFailure_ReturnsRunning()
    {
        var s = new Stub(BtStatus.Success);
        var run = new Stub(BtStatus.Running);
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireAll, s, run);
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Running);
    }

    [TestCase]
    public void Parallel_RequireAll_FailureDominatesRunning()
    {
        var fail = new Stub(BtStatus.Failure);
        var run = new Stub(BtStatus.Running);
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireAll, fail, run);
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void Parallel_RequireAll_TicksAllChildren()
    {
        var s = new Stub(BtStatus.Success);
        var fail = new Stub(BtStatus.Failure);
        var run = new Stub(BtStatus.Running);
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireAll, s, fail, run);
        par.Tick(MakeCtx());
        AssertThat(s.TickCount).IsEqual(1);
        AssertThat(fail.TickCount).IsEqual(1);
        AssertThat(run.TickCount).IsEqual(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Parallel — RequireOne
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Parallel_RequireOne_EmptyChildren_ReturnsFailure()
    {
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireOne);
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void Parallel_RequireOne_AllFailure_ReturnsFailure()
    {
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireOne,
            new Stub(BtStatus.Failure),
            new Stub(BtStatus.Failure));
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void Parallel_RequireOne_AnySuccess_ReturnsSuccess()
    {
        var fail = new Stub(BtStatus.Failure);
        var succ = new Stub(BtStatus.Success);
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireOne, fail, succ);
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Parallel_RequireOne_AnyRunning_NoSuccess_ReturnsRunning()
    {
        var fail = new Stub(BtStatus.Failure);
        var run = new Stub(BtStatus.Running);
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireOne, fail, run);
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Running);
    }

    [TestCase]
    public void Parallel_RequireOne_SuccessDominatesRunning()
    {
        var succ = new Stub(BtStatus.Success);
        var run = new Stub(BtStatus.Running);
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireOne, succ, run);
        AssertThat((int)par.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Parallel_RequireOne_TicksAllChildren()
    {
        var s = new Stub(BtStatus.Success);
        var fail = new Stub(BtStatus.Failure);
        var run = new Stub(BtStatus.Running);
        IBtTask par = Bt.Parallel(ParallelPolicy.RequireOne, s, fail, run);
        par.Tick(MakeCtx());
        AssertThat(s.TickCount).IsEqual(1);
        AssertThat(fail.TickCount).IsEqual(1);
        AssertThat(run.TickCount).IsEqual(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Decorators
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Inverter_FlipsSuccess_ToFailure()
    {
        IBtTask inv = Bt.Invert(new Stub(BtStatus.Success));
        AssertThat((int)inv.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void Inverter_FlipsFailure_ToSuccess()
    {
        IBtTask inv = Bt.Invert(new Stub(BtStatus.Failure));
        AssertThat((int)inv.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Inverter_PassesThrough_Running()
    {
        IBtTask inv = Bt.Invert(new Stub(BtStatus.Running));
        AssertThat((int)inv.Tick(MakeCtx())).IsEqual((int)BtStatus.Running);
    }

    [TestCase]
    public void Succeeder_Success_ReturnsSuccess()
    {
        IBtTask s = Bt.Succeed(new Stub(BtStatus.Success));
        AssertThat((int)s.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Succeeder_Failure_ReturnsSuccess()
    {
        IBtTask s = Bt.Succeed(new Stub(BtStatus.Failure));
        AssertThat((int)s.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Succeeder_PassesThrough_Running()
    {
        IBtTask s = Bt.Succeed(new Stub(BtStatus.Running));
        AssertThat((int)s.Tick(MakeCtx())).IsEqual((int)BtStatus.Running);
    }

    [TestCase]
    public void Failer_Success_ReturnsFailure()
    {
        IBtTask f = Bt.Fail(new Stub(BtStatus.Success));
        AssertThat((int)f.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void Failer_Failure_ReturnsFailure()
    {
        IBtTask f = Bt.Fail(new Stub(BtStatus.Failure));
        AssertThat((int)f.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void Failer_PassesThrough_Running()
    {
        IBtTask f = Bt.Fail(new Stub(BtStatus.Running));
        AssertThat((int)f.Tick(MakeCtx())).IsEqual((int)BtStatus.Running);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Leaves
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void ConditionTask_TruePredicate_ReturnsSuccess()
    {
        IBtTask cond = Bt.Condition(_ => true);
        AssertThat((int)cond.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void ConditionTask_FalsePredicate_ReturnsFailure()
    {
        IBtTask cond = Bt.Condition(_ => false);
        AssertThat((int)cond.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void ConditionTask_NeverReturnsRunning()
    {
        // Verify the result is strictly Success or Failure — not Running — for both branches.
        IBtTask t = Bt.Condition(_ => true);
        IBtTask f = Bt.Condition(_ => false);
        AssertThat(t.Tick(MakeCtx()) != BtStatus.Running).IsTrue();
        AssertThat(f.Tick(MakeCtx()) != BtStatus.Running).IsTrue();
    }

    [TestCase]
    public void ActionTask_PassthroughStatus_Running()
    {
        IBtTask act = Bt.Action(_ => BtStatus.Running);
        AssertThat((int)act.Tick(MakeCtx())).IsEqual((int)BtStatus.Running);
    }

    [TestCase]
    public void ActionTask_PassthroughStatus_Success()
    {
        IBtTask act = Bt.Action(_ => BtStatus.Success);
        AssertThat((int)act.Tick(MakeCtx())).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void ActionTask_PassthroughStatus_Failure()
    {
        IBtTask act = Bt.Action(_ => BtStatus.Failure);
        AssertThat((int)act.Tick(MakeCtx())).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void DoTask_RunsEffect_ReturnsSuccess()
    {
        bool ran = false;
        IBtTask doTask = Bt.Do(_ => { ran = true; });
        BtStatus result = doTask.Tick(MakeCtx());
        AssertThat(ran).IsTrue();
        AssertThat((int)result).IsEqual((int)BtStatus.Success);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. BehaviorTree wrapper — LastStatus tracking
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void BehaviorTree_LastStatus_DefaultIsRunning()
    {
        BehaviorTree tree = Bt.Tree(new Stub(BtStatus.Success));
        AssertThat((int)tree.LastStatus).IsEqual((int)BtStatus.Running);
    }

    [TestCase]
    public void BehaviorTree_LastStatus_UpdatedAfterTick()
    {
        BehaviorTree tree = Bt.Tree(new Stub(BtStatus.Failure));
        tree.Tick(MakeCtx());
        AssertThat((int)tree.LastStatus).IsEqual((int)BtStatus.Failure);
    }

    [TestCase]
    public void BehaviorTree_Tick_ReturnsRootResult()
    {
        BehaviorTree tree = Bt.Tree(new Stub(BtStatus.Success));
        BtStatus result = tree.Tick(MakeCtx());
        AssertThat((int)result).IsEqual((int)BtStatus.Success);
        AssertThat((int)tree.LastStatus).IsEqual((int)BtStatus.Success);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Integration: assembled tree via Bt factory with real BtContext
    // ──────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Integration_AssembledTree_ConditionFalse_SelectsFallback()
    {
        // Bt.Selector(
        //   Bt.Sequence(Bt.Condition(false), Bt.Action(Success)),
        //   Bt.Do(effect))
        // Condition fails => Sequence fails => Selector tries Do => Do succeeds.

        bool effectRan = false;
        BehaviorTree tree = Bt.Tree(
            Bt.Selector(
                Bt.Sequence(
                    Bt.Condition(_ => false),
                    Bt.Action(_ => BtStatus.Success)),
                Bt.Do(_ => { effectRan = true; })));

        BtStatus result = tree.Tick(MakeCtx());

        AssertThat((int)result).IsEqual((int)BtStatus.Success);
        AssertThat((int)tree.LastStatus).IsEqual((int)BtStatus.Success);
        AssertThat(effectRan).IsTrue();
    }

    [TestCase]
    public void Integration_AssembledTree_ConditionTrue_RunsAction()
    {
        // Bt.Selector(
        //   Bt.Sequence(Bt.Condition(true), Bt.Action(Success)),
        //   Bt.Do(effect))
        // Condition passes => Sequence's Action runs => Selector succeeds immediately.

        bool fallbackRan = false;
        bool actionRan = false;
        BehaviorTree tree = Bt.Tree(
            Bt.Selector(
                Bt.Sequence(
                    Bt.Condition(_ => true),
                    Bt.Action(_ => { actionRan = true; return BtStatus.Success; })),
                Bt.Do(_ => { fallbackRan = true; })));

        BtStatus result = tree.Tick(MakeCtx());

        AssertThat((int)result).IsEqual((int)BtStatus.Success);
        AssertThat((int)tree.LastStatus).IsEqual((int)BtStatus.Success);
        AssertThat(actionRan).IsTrue();
        AssertThat(fallbackRan).IsFalse();
    }

    [TestCase]
    public void Integration_AssembledTree_LastStatusReflectsEachTick()
    {
        // Toggle the condition between ticks; LastStatus must follow.
        bool flag = false;
        BehaviorTree tree = Bt.Tree(
            Bt.Selector(
                Bt.Condition(_ => flag),
                Bt.Fail(Bt.Action(_ => BtStatus.Success))));

        // tick 1: flag=false => condition fails => Failer turns Success to Failure => Failure
        tree.Tick(MakeCtx());
        AssertThat((int)tree.LastStatus).IsEqual((int)BtStatus.Failure);

        // tick 2: flag=true => condition succeeds => Selector returns Success
        flag = true;
        tree.Tick(MakeCtx());
        AssertThat((int)tree.LastStatus).IsEqual((int)BtStatus.Success);
    }

    [TestCase]
    public void Integration_RealAgentState_ContextPassedThrough()
    {
        // Verify that the BtContext fields are accessible inside a task.
        var rng = new DeterministicRng(99UL);
        var agent = new AgentState(7, 3, Transform3D.Identity, rng);
        var bb = new Blackboard();
        var world = new StubWorld();
        var ctx = new BtContext(agent, bb, world, 0.033);

        int capturedId = -1;
        BehaviorTree tree = Bt.Tree(Bt.Action(c =>
        {
            capturedId = c.Agent.Id;
            return BtStatus.Success;
        }));

        tree.Tick(ctx);
        AssertThat(capturedId).IsEqual(7);
    }
}

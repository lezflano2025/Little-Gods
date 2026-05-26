using Godot;
using LittleGods.Anim;
using GdUnit4;
using static GdUnit4.Assertions;

namespace LittleGods.Tests;

/// M3 P5: the spring-damped secondary-motion follower. Pure value math, headless.
[TestSuite]
public class JiggleTests
{
    private const float Dt = 1f / 60f;

    private static bool Finite(Vector3 v)
        => !float.IsNaN(v.X) && !float.IsInfinity(v.X)
        && !float.IsNaN(v.Y) && !float.IsInfinity(v.Y)
        && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);

    [TestCase]
    public void Settles_to_a_static_anchor()
    {
        var anchor = new Vector3(1f, 0f, 0f);
        var value = Vector3.Zero;
        var vel = Vector3.Zero;

        for (int i = 0; i < 1500; i++)
        {
            (value, vel) = Jiggle.Step(value, vel, anchor, JiggleParams.Default, Dt);
        }

        AssertFloat(value.DistanceTo(anchor)).IsEqualApprox(0f, 1e-2f);
        AssertFloat(vel.Length()).IsEqualApprox(0f, 1e-2f);
    }

    [TestCase]
    public void Lags_a_moving_anchor_rather_than_snapping()
    {
        // From rest, one step toward a unit anchor: the follower moves toward it
        // but does NOT reach it (secondary motion lags the driver).
        var (value, _) = Jiggle.Step(Vector3.Zero, Vector3.Zero, new Vector3(1f, 0f, 0f), JiggleParams.Default, Dt);
        AssertThat(value.X > 0f && value.X < 1f).IsTrue();
    }

    [TestCase]
    public void Underdamped_default_overshoots_then_returns()
    {
        var anchor = new Vector3(1f, 0f, 0f);
        var value = Vector3.Zero;
        var vel = Vector3.Zero;

        float maxX = 0f;
        for (int i = 0; i < 2000; i++)
        {
            (value, vel) = Jiggle.Step(value, vel, anchor, JiggleParams.Default, Dt);
            if (value.X > maxX)
            {
                maxX = value.X;
            }
        }

        // Lively (underdamped) response overshoots past the anchor at some point,
        // then settles back onto it.
        AssertThat(maxX > 1.0f).IsTrue();
        AssertFloat(value.X).IsEqualApprox(1f, 1e-2f);
    }

    [TestCase]
    public void Stable_over_many_steps_no_blow_up()
    {
        var anchor = Vector3.Zero;
        var value = new Vector3(5f, -3f, 2f); // start far off
        var vel = new Vector3(10f, 0f, -8f);  // and moving fast

        for (int i = 0; i < 4000; i++)
        {
            (value, vel) = Jiggle.Step(value, vel, anchor, JiggleParams.Default, Dt);
            AssertThat(Finite(value)).IsTrue();
        }

        // Converges to the anchor — bounded, no blow-up.
        AssertFloat(value.Length()).IsEqualApprox(0f, 1e-2f);
    }

    [TestCase]
    public void Huge_or_nonpositive_dt_is_clamped_and_stays_finite()
    {
        // A pathological dt must not produce NaN/Inf (dt is clamped internally).
        var (v1, vel1) = Jiggle.Step(Vector3.Zero, Vector3.Zero, Vector3.One, JiggleParams.Default, 1000f);
        AssertThat(Finite(v1)).IsTrue();
        AssertThat(Finite(vel1)).IsTrue();

        var (v2, vel2) = Jiggle.Step(Vector3.Zero, Vector3.Zero, Vector3.One, JiggleParams.Default, -1f);
        // Non-positive dt advances nothing.
        AssertFloat(v2.Length()).IsEqualApprox(0f, 1e-6f);
        AssertFloat(vel2.Length()).IsEqualApprox(0f, 1e-6f);
    }

    [TestCase]
    public void Step_is_deterministic()
    {
        var a = Jiggle.Step(new Vector3(0.2f, 0f, 0f), new Vector3(0f, 1f, 0f), Vector3.One, JiggleParams.Default, Dt);
        var b = Jiggle.Step(new Vector3(0.2f, 0f, 0f), new Vector3(0f, 1f, 0f), Vector3.One, JiggleParams.Default, Dt);
        AssertFloat(a.value.X).IsEqualApprox(b.value.X, 1e-7f);
        AssertFloat(a.velocity.Y).IsEqualApprox(b.velocity.Y, 1e-7f);
    }
}

using Godot;
using static Godot.Mathf;

namespace LittleGods.Anim;

/// <summary>
/// Closed-form two-bone analytic IK solver using the law of cosines.
/// M3 P1 — see docs/m3-contract.md (Agent A).
/// Pure / deterministic / immutable — no clock, no RNG.
/// All outputs are guaranteed finite for any input including zero or negative
/// lengths and target == root.
/// </summary>
public static class TwoBoneIk
{
    /// <summary>
    /// Solves one 2-bone limb via the law of cosines.
    /// </summary>
    /// <param name="root">Hip position (world).</param>
    /// <param name="upperLen">Hip-to-knee rest length (clamped to ≥ 1e-5).</param>
    /// <param name="lowerLen">Knee-to-foot rest length (clamped to ≥ 1e-5).</param>
    /// <param name="target">Desired foot position (world).</param>
    /// <param name="pole">
    /// Knee-bend hint (world position or direction); need not be normalised.
    /// Falls back to a stable default when degenerate.
    /// </param>
    /// <returns>Resolved knee and foot world positions, plus a reachability flag.</returns>
    public static IkResult Solve(
        Vector3 root,
        float upperLen,
        float lowerLen,
        Vector3 target,
        Vector3 pole)
    {
        // Guard bone lengths — never let them be zero or negative.
        float u = Max(upperLen, 1e-5f);
        float l = Max(lowerLen, 1e-5f);

        Vector3 diff = target - root;
        float dist   = diff.Length();

        bool reachable = dist >= Abs(u - l) && dist <= u + l;

        // Primary direction: root → target.
        Vector3 dir;
        if (dist < 1e-6f)
        {
            dir = Vector3.Forward; // safe default when target == root
        }
        else
        {
            dir = diff / dist;
        }

        // Pole perpendicular: project pole onto the plane perpendicular to dir.
        Vector3 polePerp = pole - dir * pole.Dot(dir);
        if (polePerp.LengthSquared() < 1e-8f)
        {
            // dir is (anti-)parallel to pole or pole is zero — pick any perpendicular.
            polePerp = StablePerpendicular(dir);
        }
        polePerp = polePerp.Normalized();

        Vector3 knee;
        Vector3 foot;

        if (reachable)
        {
            // Exact solve: law of cosines for the angle at the root. Floor the
            // distance so a degenerate target == root (reachable only when
            // u == l) cannot divide by zero — knee stays finite, foot == target.
            float d       = Max(dist, 1e-4f);
            float cosRoot = Clamp((u * u + d * d - l * l) / (2f * u * d), -1f, 1f);
            float a       = Acos(cosRoot);
            knee      = root + dir * (Cos(a) * u) + polePerp * (Sin(a) * u);
            foot      = target;
        }
        else if (dist > u + l)
        {
            // Over-reach: full extension along dir.
            knee = root + dir * u;
            foot = root + dir * (u + l);
        }
        else
        {
            // Under-reach (dist < |u - l|): fold to minimum extension.
            float effective = Max(Abs(u - l), 1e-4f);
            float cosRoot   = Clamp((u * u + effective * effective - l * l) / (2f * u * effective), -1f, 1f);
            float a         = Acos(cosRoot);
            knee = root + dir * (Cos(a) * u) + polePerp * (Sin(a) * u);
            foot = root + dir * effective;
        }

        return new IkResult(knee, foot, reachable);
    }

    // Returns any vector that is perpendicular to v and has non-zero length.
    private static Vector3 StablePerpendicular(Vector3 v)
    {
        // Choose the axis least aligned with v to avoid near-parallel cross product.
        float ax = Abs(v.X);
        float ay = Abs(v.Y);
        float az = Abs(v.Z);

        Vector3 basis = (ax <= ay && ax <= az)
            ? Vector3.Right    // (1,0,0)
            : (ay <= az)
                ? Vector3.Up   // (0,1,0)
                : Vector3.Back; // (0,0,-1)

        Vector3 perp = v.Cross(basis);
        // Cross product of non-parallel unit-ish vectors is always non-zero,
        // but guard just in case v was degenerate.
        if (perp.LengthSquared() < 1e-12f)
        {
            perp = Vector3.Up;
        }
        return perp;
    }
}

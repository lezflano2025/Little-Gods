using System;
using Godot;

namespace LittleGods.Anim;

/// A skeletal pose expressed as per-bone LOCAL transform deltas applied on top
/// of each bone's rest pose:
///
///     posedLocal_i = restLocal_i * Delta(i)
///
/// An identity delta therefore leaves a bone at rest (undeformed). The
/// locomotion driver (M3 P4) builds one Pose per tick from the IK results;
/// CreaturePreview.ApplyPose (M3 P0) writes it onto the Skeleton3D.
///
/// Immutable: <see cref="With"/> returns a new Pose (copy-on-write). Deltas are
/// indexed by CreatureSkeleton bone index.
public readonly struct Pose
{
    private readonly Transform3D[]? _deltas;

    /// Wraps a per-bone delta array (index = bone index). Takes ownership; a
    /// null array yields an empty pose. Callers building bulk poses use this.
    public Pose(Transform3D[]? deltas)
    {
        _deltas = deltas;
    }

    /// Number of bones this pose addresses.
    public int BoneCount => _deltas?.Length ?? 0;

    /// All-identity pose for a skeleton with <paramref name="boneCount"/> bones
    /// (the rest pose — applying it leaves the mesh undeformed).
    public static Pose Rest(int boneCount)
    {
        var deltas = new Transform3D[boneCount < 0 ? 0 : boneCount];
        for (int i = 0; i < deltas.Length; i++)
        {
            deltas[i] = Transform3D.Identity;
        }
        return new Pose(deltas);
    }

    /// Local delta for bone <paramref name="boneIndex"/>. Returns identity for
    /// an out-of-range index or an empty pose, so callers can apply unposed
    /// bones safely.
    public Transform3D Delta(int boneIndex)
        => _deltas != null && boneIndex >= 0 && boneIndex < _deltas.Length
            ? _deltas[boneIndex]
            : Transform3D.Identity;

    /// Returns a copy with bone <paramref name="boneIndex"/>'s delta replaced.
    /// Grows the pose if the index is beyond the current bone count (new slots
    /// default to identity). The receiver is left unchanged (immutable).
    public Pose With(int boneIndex, Transform3D delta)
    {
        if (boneIndex < 0)
        {
            return this;
        }

        int n = Math.Max(BoneCount, boneIndex + 1);
        var deltas = new Transform3D[n];
        for (int i = 0; i < n; i++)
        {
            deltas[i] = Delta(i);
        }
        deltas[boneIndex] = delta;
        return new Pose(deltas);
    }
}

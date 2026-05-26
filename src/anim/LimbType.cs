namespace LittleGods.Anim;

/// How a resolved limb chain functions, decided by LimbClassifier (M3 P2) from
/// the parent slot plus the chain's world orientation. The gait controller
/// drives only the chains classified as load-bearing <see cref="Leg"/>s.
///
/// Classification is separate from resolution: SkeletonResolver records the
/// LimbChain topology; the type below is assigned later by the classifier.
public enum LimbType
{
    /// Load-bearing, gait-driven, foot planted on the M3 ground plane.
    Leg,

    /// Lateral / upward limb, not load-bearing (manipulation is post-M3).
    Arm,

    /// Upward or lateral limb that flaps (flight animation deferred).
    Wing,

    /// The tail slot — secondary jiggle (M3 P5), never gait.
    Tail,

    /// Unclassified / unknown role.
    Other,
}

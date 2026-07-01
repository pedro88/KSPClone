#nullable enable annotations

namespace KSPClone.Construction
{
    /// <summary>
    /// A part's local transform relative to its parent's attach point: position
    /// offset (metres) + orientation (unit quaternion). Plain data — the
    /// Construction assembly is engine- and math-library-agnostic (Art. 7); the
    /// launch boundary (M3-T08) converts this to the flight-side transform. No
    /// rotation math lives here on purpose.
    /// </summary>
    public readonly struct PartPose
    {
        public readonly double Px, Py, Pz;
        public readonly double Qx, Qy, Qz, Qw;

        public PartPose(double px, double py, double pz, double qx, double qy, double qz, double qw)
        {
            Px = px; Py = py; Pz = pz;
            Qx = qx; Qy = qy; Qz = qz; Qw = qw;
        }

        /// <summary>Zero offset, identity rotation.</summary>
        public static readonly PartPose Identity = new(0, 0, 0, 0, 0, 0, 1);
    }
}

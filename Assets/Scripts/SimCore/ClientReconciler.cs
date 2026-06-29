#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Reconciles the client's predicted state against an authoritative
    /// server snapshot (M1-T11, NET-3).
    ///
    /// Two thresholds drive the smoothing strategy (per Fiedler State
    /// Synchronization, netcode reference §2):
    ///  - SmallErrorMeters: below this, smooth the visual correction
    ///    over a few frames (no single-frame visible pop).
    ///  - HardSnapMeters: above this, snap the visible transform
    ///    immediately — a big desync looks worse smeared than snapped.
    ///
    /// The reconciler emits a <see cref="ReconciliationDecision"/>
    /// describing what to do; the rendering layer applies it.
    /// </summary>
    public sealed class ClientReconciler
    {
        public double SmallErrorMeters { get; }
        public double HardSnapMeters { get; }

        public ClientReconciler(double smallErrorMeters = 0.25, double hardSnapMeters = 1.0)
        {
            if (smallErrorMeters < 0.0) throw new ArgumentOutOfRangeException(nameof(smallErrorMeters));
            if (hardSnapMeters < smallErrorMeters) throw new ArgumentOutOfRangeException(nameof(hardSnapMeters));
            SmallErrorMeters = smallErrorMeters;
            HardSnapMeters = hardSnapMeters;
        }

        /// <summary>
        /// Compute the visual correction to apply for the divergence
        /// between <paramref name="preReplay"/> (what the client had
        /// rendered) and the post-replay predicted state.
        /// </summary>
        public ReconciliationDecision Decide(PredictedVesselState preReplay, PredictedVesselState postReplay, double measuredRttSeconds)
        {
            var err = postReplay.Position - preReplay.Position;
            var errMag = err.Length;

            // Beyond 150 ms (NET-6) widen the smoothing window and
            // cap the per-frame catch-up so corrections stay visually
            // continuous rather than repeatedly snapping.
            var smoothingFrames = measuredRttSeconds <= 0.150 ? 4 : 10;
            var perFrameCorrectionMag = measuredRttSeconds <= 0.150 ? errMag / smoothingFrames
                                                                       : System.Math.Min(errMag, HardSnapMeters) / smoothingFrames;

            if (errMag <= SmallErrorMeters)
                return new ReconciliationDecision(ReconciliationKind.None, err, perFrameCorrectionMag, smoothingFrames);
            if (errMag >= HardSnapMeters)
                return new ReconciliationDecision(ReconciliationKind.HardSnap, err, errMag, 0);
            return new ReconciliationDecision(ReconciliationKind.Smooth, err, perFrameCorrectionMag, smoothingFrames);
        }
    }

    public enum ReconciliationKind
    {
        /// <summary>Divergence below the smoothing threshold — no visible correction.</summary>
        None,
        /// <summary>Small-to-medium divergence — blend the correction over N frames.</summary>
        Smooth,
        /// <summary>Large divergence — snap immediately.</summary>
        HardSnap
    }

    public readonly struct ReconciliationDecision
    {
        public ReconciliationKind Kind { get; }
        public Vector3d Correction { get; }
        public double PerFrameCorrectionMagnitude { get; }
        public int SmoothingFrames { get; }

        public ReconciliationDecision(ReconciliationKind kind, Vector3d correction, double perFrameCorrectionMagnitude, int smoothingFrames)
        {
            Kind = kind;
            Correction = correction;
            PerFrameCorrectionMagnitude = perFrameCorrectionMagnitude;
            SmoothingFrames = smoothingFrames;
        }
    }
}
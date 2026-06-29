#nullable enable annotations

using KSPClone.SimCore;

namespace KSPClone.Server
{
    /// <summary>
    /// Adapts the UnityEngine-coupled <see cref="BubbleIntegrator"/> to the
    /// engine-agnostic <see cref="IBubbleStepper"/> seam (ADR-0014 §1), so
    /// <see cref="ServerSimulation"/> can drive the active-physics step at the
    /// fixed point in its tick without referencing UnityEngine. The integrator
    /// owns the fixed dt; the seam's <paramref name="dtSeconds"/> is the same
    /// 1/60 s and is not re-threaded.
    /// </summary>
    public sealed class BubbleIntegratorStepper : IBubbleStepper
    {
        private readonly BubbleIntegrator _integrator;

        public BubbleIntegratorStepper(BubbleIntegrator integrator)
            => _integrator = integrator;

        public void Step(double dtSeconds) => _integrator.Step();
    }
}

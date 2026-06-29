#nullable enable annotations

namespace KSPClone.SimCore
{
    /// <summary>
    /// The one engine-coupled step in the fixed tick (ADR-0014 §1). The
    /// active-physics integration — apply gravity + thrust, advance the
    /// bubbles' physics scenes, read transforms back into the
    /// authoritative doubles, rebase the floating origin — touches PhysX
    /// and therefore lives in the Server assembly (ADR-0009). SimCore
    /// only knows this seam: <see cref="ServerSimulation"/> calls
    /// <see cref="Step"/> at the fixed point between the clustering pass
    /// and the demotion/suspension passes, never importing UnityEngine.
    ///
    /// Headless and unit-test runs use <see cref="NullBubbleStepper"/> so
    /// the whole tick sequence runs without an engine; the live server
    /// injects an adapter wrapping <c>BubbleIntegrator</c>.
    /// </summary>
    public interface IBubbleStepper
    {
        /// <summary>Advance every live bubble by one fixed tick.</summary>
        void Step(double dtSeconds);
    }

    /// <summary>
    /// No-op stepper. Active vessels keep their last cached state; nothing
    /// integrates. Used by headless servers and SimCore tests (ADR-0014 §1).
    /// </summary>
    public sealed class NullBubbleStepper : IBubbleStepper
    {
        public static readonly NullBubbleStepper Instance = new();
        public void Step(double dtSeconds) { }
    }
}

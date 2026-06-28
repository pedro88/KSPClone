#nullable enable annotations

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using KSPClone.SimCore;

namespace KSPClone.Server
{
    /// <summary>
    /// Server-side Unity adapter that materialises one
    /// <see cref="PhysicsScene"/> per <see cref="PhysicsBubble"/> so
    /// active vessels in different bubbles never see each other's
    /// colliders (ADR-0003, ADR-0012 §6).
    ///
    /// Wires to the engine-agnostic sim core by listening to the
    /// <see cref="BubbleRegistry.BubbleCreated"/> and
    /// <see cref="BubbleDestroyed"/> events. When a bubble is created,
    /// a fresh Unity <see cref="Scene"/> with
    /// <see cref="LocalPhysicsMode.Physics3D"/> is allocated and its
    /// <see cref="PhysicsScene"/> handle is attached to the bubble via
    /// <see cref="PhysicsBubble.AttachScene"/>. On destruction the
    /// scene is unloaded and the handle cleared.
    ///
    /// This component lives in <see cref="KSPClone.Server"/> (not in
    /// <see cref="KSPClone.SimCore"/>) because it depends on UnityEngine.
    /// </summary>
    public sealed class UnityBubbleHost : IDisposable
    {
        private readonly BubbleRegistry _registry;
        private readonly Dictionary<BubbleId, Scene> _bubbleScenes = new();
        private bool _disposed;

        public UnityBubbleHost(BubbleRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _registry.BubbleCreated += OnBubbleCreated;
            _registry.BubbleDestroyed += OnBubbleDestroyed;
        }

        public PhysicsScene? TryGetPhysicsScene(BubbleId bubbleId)
        {
            if (_bubbleScenes.TryGetValue(bubbleId, out var scene))
                return scene.GetPhysicsScene();
            return null;
        }

        private void OnBubbleCreated(PhysicsBubble bubble)
        {
            if (_disposed) return;
            var scene = SceneManager.CreateScene(
                $"Bubble_{bubble.Id}",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _bubbleScenes[bubble.Id] = scene;
            bubble.AttachScene(new BubbleSceneHandle(Guid.NewGuid()));
            // The actual Scene→BubbleSceneHandle mapping is held here. SimCore only
            // sees the opaque Guid token; Unity-side lookups go through TryGetPhysicsScene.
        }

        private void OnBubbleDestroyed(PhysicsBubble bubble)
        {
            if (_bubbleScenes.TryGetValue(bubble.Id, out var scene))
            {
                if (scene.IsValid() && scene.isLoaded)
                    SceneManager.UnloadSceneAsync(scene);
                _bubbleScenes.Remove(bubble.Id);
            }
            bubble.DetachScene();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _registry.BubbleCreated -= OnBubbleCreated;
            _registry.BubbleDestroyed -= OnBubbleDestroyed;
            foreach (var scene in _bubbleScenes.Values)
            {
                if (scene.IsValid() && scene.isLoaded)
                    SceneManager.UnloadSceneAsync(scene);
            }
            _bubbleScenes.Clear();
        }
    }
}
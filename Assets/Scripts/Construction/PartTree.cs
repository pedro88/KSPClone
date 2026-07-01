#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.Construction
{
    /// <summary>
    /// The parts of a <see cref="Design"/> arranged parent→child, indexed by
    /// stable <see cref="NodeId"/>. Holds the node set, a parent→children index,
    /// and the root; exposes lookup, child enumeration, subtree enumeration and
    /// ancestor walk (M3-T01). Structural mutation is deliberately minimal here —
    /// the validated edit-op semantics (attach-point occupancy, cycle guard) live
    /// in the pure mutator (M3-T02).
    ///
    /// No world state, no engine types: this is design-time only (Art. 7).
    /// </summary>
    public sealed class PartTree
    {
        private readonly Dictionary<NodeId, PartNode> _nodes = new();
        private readonly Dictionary<NodeId, List<NodeId>> _children = new();

        public NodeId Root { get; }
        public int Count => _nodes.Count;

        public PartTree(PartNode root)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            if (!root.IsRoot) throw new ArgumentException("Root node must have Parent = NodeId.None.", nameof(root));
            Root = root.Id;
            _nodes[root.Id] = root;
            _children[root.Id] = new List<NodeId>();
        }

        private PartTree(NodeId root) { Root = root; }

        public bool TryGet(NodeId id, out PartNode node) => _nodes.TryGetValue(id, out node!);
        public bool Contains(NodeId id) => _nodes.ContainsKey(id);

        /// <summary>Direct children of <paramref name="id"/>, in insertion order (empty if leaf/unknown).</summary>
        public IReadOnlyList<NodeId> Children(NodeId id) =>
            _children.TryGetValue(id, out var kids) ? kids : Array.Empty<NodeId>();

        /// <summary>Depth-first enumeration of a node and all its descendants (self first, deterministic order).</summary>
        public IEnumerable<NodeId> Subtree(NodeId id)
        {
            if (!_nodes.ContainsKey(id)) yield break;
            var stack = new Stack<NodeId>();
            stack.Push(id);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                yield return n;
                var kids = _children[n];
                // Push in reverse so children pop in insertion order.
                for (int i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
            }
        }

        /// <summary>Walk from a node's parent up to the root (excludes the node itself).</summary>
        public IEnumerable<NodeId> Ancestors(NodeId id)
        {
            if (!_nodes.TryGetValue(id, out var node)) yield break;
            var cur = node.Parent;
            while (!cur.IsNone && _nodes.TryGetValue(cur, out var parent))
            {
                yield return cur;
                cur = parent.Parent;
            }
        }

        /// <summary>True if <paramref name="ancestor"/> is <paramref name="id"/> or one of its ancestors.</summary>
        public bool IsSelfOrAncestor(NodeId ancestor, NodeId id)
        {
            if (ancestor.Equals(id)) return true;
            foreach (var a in Ancestors(id)) if (a.Equals(ancestor)) return true;
            return false;
        }

        // --- structural mutation (used by the builder + the M3-T02 mutator) ---

        /// <summary>Attach an already-parented node under an existing parent.</summary>
        public void Add(PartNode node)
        {
            if (node is null) throw new ArgumentNullException(nameof(node));
            if (node.IsRoot) throw new ArgumentException("Use the constructor to seed the root.", nameof(node));
            if (_nodes.ContainsKey(node.Id)) throw new ArgumentException($"Duplicate node id {node.Id}.", nameof(node));
            if (!_nodes.ContainsKey(node.Parent)) throw new ArgumentException($"Unknown parent {node.Parent}.", nameof(node));
            _nodes[node.Id] = node;
            _children[node.Id] = new List<NodeId>();
            _children[node.Parent].Add(node.Id);
        }

        /// <summary>Remove a node and its whole subtree. The root cannot be removed.</summary>
        public void RemoveSubtree(NodeId id)
        {
            if (id.Equals(Root)) throw new ArgumentException("Cannot remove the root node.", nameof(id));
            if (!_nodes.TryGetValue(id, out var node)) return;
            // Collect the subtree first (enumeration reads the index we're about to edit).
            var doomed = new List<NodeId>();
            foreach (var n in Subtree(id)) doomed.Add(n);
            foreach (var n in doomed) { _nodes.Remove(n); _children.Remove(n); }
            if (_children.TryGetValue(node.Parent, out var siblings)) siblings.Remove(id);
        }

        /// <summary>Re-parent a node's record (its subtree follows, since children index by id).</summary>
        public void Reparent(PartNode moved)
        {
            if (!_nodes.TryGetValue(moved.Id, out var old))
                throw new ArgumentException($"Unknown node {moved.Id}.", nameof(moved));
            if (!_nodes.ContainsKey(moved.Parent))
                throw new ArgumentException($"Unknown new parent {moved.Parent}.", nameof(moved));
            if (_children.TryGetValue(old.Parent, out var oldSiblings)) oldSiblings.Remove(moved.Id);
            _nodes[moved.Id] = moved;
            _children[moved.Parent].Add(moved.Id);
        }

        /// <summary>Deep copy — an immutable snapshot for launch (M3-T08) or persistence.</summary>
        public PartTree Clone()
        {
            var copy = new PartTree(Root);
            foreach (var kv in _nodes) copy._nodes[kv.Key] = kv.Value; // PartNode is immutable
            foreach (var kv in _children) copy._children[kv.Key] = new List<NodeId>(kv.Value);
            return copy;
        }
    }
}

#nullable enable annotations

using UnityEngine;
using KSPClone.Construction;

namespace KSPClone.Client
{
    /// <summary>
    /// Identifies a VAB preview object under the mouse-raycast: either a part body
    /// (select it) or an attach-point marker (place the armed part there).
    /// </summary>
    public sealed class VabPartTag : MonoBehaviour
    {
        public NodeId Node;
        public string AttachKey = "";
        public bool IsMarker;
    }
}

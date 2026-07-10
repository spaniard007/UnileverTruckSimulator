using System;
using UnityEngine;

namespace GeNa.Core
{
    /// <summary>
    /// Static event hub for GeNa terrain/runtime notifications. Publishers (e.g.
    /// <see cref="GeNaTerrainEvents"/>) raise these; other systems may subscribe.
    /// </summary>
    public static class GeNaEvents
    {
        /// <summary>
        /// Raised when a Unity Terrain reports a change. Args: the changed Terrain and
        /// the <see cref="TerrainChangedFlags"/> describing what changed.
        /// </summary>
        public static Action<Terrain, TerrainChangedFlags> onTerrainChanged;
    }
}

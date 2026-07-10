using UnityEngine;
using NWH.VehiclePhysics2;

namespace BLI.CMP
{
    /// <summary>
    /// Press the FlipOver key (M by default) to instantly recover the vehicle:
    /// it is teleported onto the nearest CLEAR patch of road, set upright, and turned to
    /// face the direction it was last travelling in.
    ///
    /// This replaces NWH's in-place "gradual flip over", which only rotated the vehicle
    /// where it lay (often still off the road, in a ditch, or upside down on terrain).
    ///
    /// A candidate spot is only accepted if the vehicle actually FITS there — otherwise
    /// getting stuck on a concrete divider would just respawn you back onto the same
    /// divider, because the road surface underneath it is still valid road.
    ///
    /// Road detection is by tag. Note the Terrain in this scene is *also* tagged 'Road',
    /// so TerrainColliders are explicitly excluded — otherwise every patch of ground
    /// would count as road.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class FlipOverToRoad : MonoBehaviour
    {
        [Header("Road Detection")]
        [Tooltip("Tag used by road meshes.")]
        public string roadTag = "Road";

        [Tooltip("Ignore TerrainColliders even if they carry the road tag (they are not road).")]
        public bool excludeTerrain = true;

        [Tooltip("How far out to search for a clear road spot (metres).")]
        public float searchRadius = 80f;

        [Tooltip("Spacing of the search samples (metres). Smaller = more accurate, slower.")]
        public float searchStep = 3f;

        [Tooltip("How far above/below each sample point to cast when looking for road.")]
        public float rayUp = 30f;
        public float rayDown = 80f;

        [Tooltip("Reject road surfaces steeper than this (degrees) — e.g. the sloped side of a divider.")]
        public float maxSurfaceSlope = 30f;

        [Header("Fit Check")]
        [Tooltip("Refuse to respawn somewhere the vehicle doesn't fit (divider, barrier, wall, another car).")]
        public bool requireClearSpace = true;

        [Tooltip("Shrink the fit-test box's width/length slightly so it doesn't catch the road edge.")]
        [Range(0.5f, 1f)]
        public float fitBoxScale = 0.85f;

        [Tooltip("How far above the wheels the fit box starts (metres). Just enough to ignore the " +
                 "road surface — anything taller than this (kerb, divider, barrier) is detected.")]
        public float groundSkip = 0.15f;

        [Tooltip("Tags to ignore when testing whether a spot is blocked.")]
        public string[] ignoreBlockingTags = { "Road", "Terrain", "Trailer" };

        [Tooltip("Log what blocked each rejected spot (useful while tuning).")]
        public bool logBlockers = false;

        /// <summary>Which way the vehicle should point after recovery.</summary>
        public enum RecoverFacing
        {
            /// <summary>Align with the road, going the same way you were headed. Best for driving off immediately.</summary>
            RoadDirection,
            /// <summary>Face the direction the vehicle was last moving (can be sideways if you crashed sliding).</summary>
            TravelDirection,
            /// <summary>Keep whatever way the vehicle is already pointing.</summary>
            CurrentHeading,
            /// <summary>Always face a fixed world direction you set below.</summary>
            CustomWorldDirection,
        }

        [Header("Placement")]
        [Tooltip("Extra clearance above the road surface after placement (metres).")]
        public float clearance = 0.25f;

        [Tooltip("Which way the vehicle points after recovery.")]
        public RecoverFacing facing = RecoverFacing.RoadDirection;

        [Tooltip("Used only when Facing = Custom World Direction.")]
        public Vector3 customWorldDirection = Vector3.forward;

        [Tooltip("Speed (m/s) above which the travel direction is remembered.")]
        public float travelDirMinSpeed = 1f;

        [Tooltip("How far to follow the road when working out which way it runs (metres).")]
        public float roadProbeMaxDistance = 30f;

        [Tooltip("Step size while following the road (metres). Smaller = more accurate, slower.")]
        public float roadProbeStep = 3f;

        [Tooltip("Angular resolution of the road-direction search (degrees). Lower = more accurate.")]
        [Range(2f, 15f)]
        public float roadProbeAngleStep = 5f;

        [Tooltip("If the road runs out ahead but continues behind, face the other way instead of a dead end.")]
        public bool turnAroundAtDeadEnd = true;

        [Tooltip("Zero out velocity so the vehicle doesn't keep tumbling after recovery.")]
        public bool resetVelocity = true;

        [Header("Behaviour")]
        [Tooltip("Only recover when the vehicle is actually flipped over. Off = M always recovers.")]
        public bool onlyWhenFlipped = false;

        [Tooltip("Seconds before M can be used again.")]
        public float cooldown = 0.5f;

        private VehicleController _vc;
        private Rigidbody _rb;
        private Vector3 _lastTravelDir = Vector3.forward;
        private float _bottomDrop = 1f;    // pivot -> lowest collider point
        private Vector3 _boxCenterLocal;   // fit-test box, in vehicle local space
        private Vector3 _boxHalf;
        private bool _prevPressed;
        private float _lastRecoverTime = -999f;

        // NWH flip module, so we can clear its "flippedOver" latch after we recover.
        private object _flipModule;
        private System.Reflection.FieldInfo _flippedOverField;

        private void Start()
        {
            _vc = GetComponent<VehicleController>();
            _rb = GetComponent<Rigidbody>();
            _lastTravelDir = transform.forward;

            MeasureVehicle();
            CacheFlipModule();
        }

        /// <summary>Measure ride height and the body box used for the "does it fit" test.</summary>
        private void MeasureVehicle()
        {
            Bounds b = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!any) { _boxHalf = new Vector3(1f, 1f, 2f); _boxCenterLocal = Vector3.up; return; }

            _bottomDrop = Mathf.Max(0.1f, transform.position.y - b.min.y);

            Vector3 ext = b.extents;
            Vector3 localCenter = transform.InverseTransformPoint(b.center);

            // The box must span from just above the wheels up to the roof. Starting it any
            // higher leaves a blind gap where a kerb or concrete divider hides — which is
            // exactly how the vehicle used to get respawned back on top of a divider.
            float lowestY = -_bottomDrop;              // lowest collider point, local
            float topY    = lowestY + ext.y * 2f;      // roof, local
            float bottomY = lowestY + groundSkip;      // skip the road surface itself
            if (topY - bottomY < 0.2f) topY = bottomY + 0.2f;

            _boxHalf = new Vector3(ext.x * fitBoxScale,
                                   (topY - bottomY) * 0.5f,
                                   ext.z * fitBoxScale);
            _boxCenterLocal = new Vector3(localCenter.x, (bottomY + topY) * 0.5f, localCenter.z);
        }

        private void CacheFlipModule()
        {
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "FlipOverModuleWrapper") continue;
                var modField = mb.GetType().GetField("module");
                if (modField != null)
                {
                    _flipModule = modField.GetValue(mb);
                    if (_flipModule != null)
                        _flippedOverField = _flipModule.GetType().GetField("flippedOver");
                }
                break;
            }
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;
            Vector3 v = _rb.linearVelocity;
            v.y = 0f;
            if (v.sqrMagnitude > travelDirMinSpeed * travelDirMinSpeed)
                _lastTravelDir = v.normalized;
        }

        private void Update()
        {
            if (_vc == null || !_vc.IsInitialized) return;

            bool pressed = _vc.input.FlipOver;   // bound to M in the NWH input actions
            bool edge = pressed && !_prevPressed;
            _prevPressed = pressed;

            if (!edge) return;
            if (Time.time - _lastRecoverTime < cooldown) return;
            if (onlyWhenFlipped && !IsFlipped()) return;

            _lastRecoverTime = Time.time;
            RecoverToRoad();
        }

        private bool IsFlipped()
        {
            return Vector3.Angle(transform.up, Vector3.up) > 70f;
        }

        /// <summary>Teleport onto the nearest CLEAR road, upright, facing per <see cref="facing"/>.</summary>
        public void RecoverToRoad()
        {
            Vector3 pos; Quaternion rot; Vector3 surfacePoint, surfaceUp;
            if (!TryFindClearRoadPose(transform.position, out pos, out rot, out surfacePoint, out surfaceUp))
            {
                Debug.LogWarning("[FlipOverToRoad] No clear road found within " + searchRadius +
                                 "m — uprighting in place instead.", this);
                UprightInPlace();
                ClearFlipLatch();
                return;
            }

            // Align to the road only after we have a confirmed clear spot: working out the
            // road's tangent costs a couple hundred raycasts, so we do it once, not per candidate.
            if (facing == RecoverFacing.RoadDirection)
            {
                Vector3 tangent;
                if (TryGetRoadDirection(surfacePoint, ReferenceDirection(), out tangent))
                {
                    Vector3 f = Vector3.ProjectOnPlane(tangent, surfaceUp);
                    if (f.sqrMagnitude > 0.001f)
                    {
                        Quaternion roadRot = Quaternion.LookRotation(f.normalized, surfaceUp);
                        // Turning the vehicle changes its footprint — make sure it still fits.
                        if (!requireClearSpace || IsPoseClear(pos, roadRot))
                            rot = roadRot;
                        else if (logBlockers)
                            Debug.Log("[FlipOverToRoad] Road-aligned heading didn't fit; kept fallback heading.", this);
                    }
                }
                else if (logBlockers)
                {
                    Debug.Log("[FlipOverToRoad] Couldn't determine road direction; kept fallback heading.", this);
                }
            }

            Place(pos, rot);
            ClearFlipLatch();
        }

        /// <summary>The heading we aim for (also the tie-breaker for which way along the road to face).</summary>
        private Vector3 ReferenceDirection()
        {
            Vector3 r;
            switch (facing)
            {
                case RecoverFacing.CurrentHeading:        r = transform.forward; break;
                case RecoverFacing.CustomWorldDirection:  r = customWorldDirection; break;
                default:                                  r = _lastTravelDir; break;  // Travel + Road
            }
            r.y = 0f;
            if (r.sqrMagnitude < 0.001f) r = transform.forward;
            if (r.sqrMagnitude < 0.001f) r = Vector3.forward;
            return r.normalized;
        }

        /// <summary>
        /// Estimate which way the road runs at <paramref name="p"/> by probing outward in every
        /// direction: a road is a strip, so the axis with road on BOTH sides at several distances
        /// is the road's tangent. The sign is chosen to keep going the way you were headed.
        /// </summary>
        public bool TryGetRoadDirection(Vector3 p, Vector3 reference, out Vector3 dir)
        {
            dir = Vector3.zero;
            float step = Mathf.Max(0.5f, roadProbeStep);
            float angStep = Mathf.Max(2f, roadProbeAngleStep);

            float bestTotal = 0f;
            Vector3 bestAxis = Vector3.zero;
            float bestFwdReach = 0f, bestBackReach = 0f;

            // A road is a long strip: the axis the road stretches furthest along IS its tangent.
            for (float deg = 0f; deg < 180f; deg += angStep)
            {
                float a = deg * Mathf.Deg2Rad;
                Vector3 axis = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

                float fwd = RoadReach(p, axis, step);
                float back = RoadReach(p, -axis, step);
                float total = fwd + back;

                if (total > bestTotal)
                {
                    bestTotal = total; bestAxis = axis;
                    bestFwdReach = fwd; bestBackReach = back;
                }
            }

            // Need the road to actually go somewhere, else we can't call it a direction.
            if (bestTotal < step * 2f) return false;

            // Prefer the way you were heading; but if the road clearly runs out that way and
            // continues the other, turn around rather than face a dead end.
            float dot = Vector3.Dot(bestAxis, reference);
            Vector3 preferred = dot >= 0f ? bestAxis : -bestAxis;
            float preferredReach = dot >= 0f ? bestFwdReach : bestBackReach;
            float otherReach = dot >= 0f ? bestBackReach : bestFwdReach;

            if (turnAroundAtDeadEnd && otherReach > preferredReach * 2f && otherReach > step * 2f)
                preferred = -preferred;

            dir = preferred;
            return true;
        }

        /// <summary>How far the road continues from <paramref name="p"/> along <paramref name="dir"/>.</summary>
        private float RoadReach(Vector3 p, Vector3 dir, float step)
        {
            float reach = 0f;
            RaycastHit h;
            for (float d = step; d <= roadProbeMaxDistance; d += step)
            {
                if (!RaycastRoad(p + dir * d, out h)) break;
                reach = d;
            }
            return reach;
        }

        private void Place(Vector3 pos, Quaternion rot)
        {
            if (resetVelocity)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.position = pos;
            _rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
        }

        private void UprightInPlace()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            Place(transform.position + Vector3.up * 0.5f, Quaternion.LookRotation(fwd, Vector3.up));
        }

        // Stops NWH's module from then running its own 3-second gradual flip on the
        // vehicle we just placed correctly.
        private void ClearFlipLatch()
        {
            if (_flipModule != null && _flippedOverField != null)
                _flippedOverField.SetValue(_flipModule, false);
        }

        // ── Road search ──────────────────────────────────────────────────────────

        /// <summary>
        /// Nearest road spot where the vehicle actually fits. Checks directly beneath
        /// first, then expands in rings. A spot is rejected if the surface is too steep
        /// or if anything solid (divider, barrier, other vehicle) occupies the space.
        /// </summary>
        private bool TryFindClearRoadPose(Vector3 origin, out Vector3 pos, out Quaternion rot,
                                          out Vector3 surfacePoint, out Vector3 surfaceUp)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            surfacePoint = Vector3.zero; surfaceUp = Vector3.up;

            RaycastHit h;
            if (RaycastRoad(origin, out h) && TryBuildPose(h, out pos, out rot))
            {
                surfacePoint = h.point; surfaceUp = h.normal;
                return true;
            }

            for (float r = searchStep; r <= searchRadius; r += searchStep)
            {
                int steps = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / searchStep));
                float bestSqr = float.MaxValue;
                bool found = false;
                Vector3 bp = Vector3.zero, bsp = Vector3.zero, bsu = Vector3.up;
                Quaternion br = Quaternion.identity;

                for (int i = 0; i < steps; i++)
                {
                    float a = (i / (float)steps) * Mathf.PI * 2f;
                    Vector3 p = origin + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                    RaycastHit hh;
                    if (!RaycastRoad(p, out hh)) continue;

                    Vector3 cp; Quaternion cr;
                    if (!TryBuildPose(hh, out cp, out cr)) continue;   // steep or blocked

                    float d = (cp - origin).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; bp = cp; br = cr; bsp = hh.point; bsu = hh.normal; found = true; }
                }

                if (found)
                {
                    pos = bp; rot = br; surfacePoint = bsp; surfaceUp = bsu;
                    return true;   // nearest clear ring wins
                }
            }
            return false;
        }

        /// <summary>Turn a road hit into a final pose, rejecting steep or occupied spots.</summary>
        private bool TryBuildPose(RaycastHit hit, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;

            Vector3 up = hit.normal;
            if (Vector3.Angle(up, Vector3.up) > maxSurfaceSlope) return false;

            Vector3 fwd = Vector3.ProjectOnPlane(ReferenceDirection(), up);
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            fwd.Normalize();

            pos = hit.point + up * (_bottomDrop + clearance);
            rot = Quaternion.LookRotation(fwd, up);

            if (requireClearSpace && !IsPoseClear(pos, rot)) return false;
            return true;
        }

        /// <summary>True when nothing solid occupies the vehicle's body box at this pose.</summary>
        public bool IsPoseClear(Vector3 pos, Quaternion rot)
        {
            Vector3 center = pos + rot * _boxCenterLocal;
            Collider[] cols = Physics.OverlapBox(center, _boxHalf, rot, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null) continue;
                if (c.transform.IsChildOf(transform)) continue;           // ourselves
                if (excludeTerrain && c is TerrainCollider) continue;     // ground
                if (IsIgnoredTag(c.tag)) continue;                        // road surface etc.

                if (logBlockers)
                    Debug.Log("[FlipOverToRoad] spot blocked by '" + c.gameObject.name + "' (tag=" + c.tag + ")", this);
                return false;
            }
            return true;
        }

        private bool IsIgnoredTag(string tag)
        {
            if (ignoreBlockingTags == null) return false;
            for (int i = 0; i < ignoreBlockingTags.Length; i++)
                if (ignoreBlockingTags[i] == tag) return true;
            return false;
        }

        /// <summary>Cast down at a point and return the topmost ROAD surface (never terrain).</summary>
        private bool RaycastRoad(Vector3 p, out RaycastHit hit)
        {
            hit = default(RaycastHit);
            Vector3 start = p + Vector3.up * rayUp;

            RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, rayUp + rayDown,
                                                   ~0, QueryTriggerInteraction.Ignore);
            float bestY = float.MinValue;
            bool ok = false;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (!h.collider.CompareTag(roadTag)) continue;
                if (excludeTerrain && h.collider is TerrainCollider) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;

                if (h.point.y > bestY) { bestY = h.point.y; hit = h; ok = true; }
            }
            return ok;
        }
    }
}

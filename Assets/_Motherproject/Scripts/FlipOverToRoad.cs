using UnityEngine;
using NWH.VehiclePhysics2;

namespace BLI.CMP
{
    /// <summary>
    /// Press the FlipOver key (M by default) to instantly recover the vehicle:
    /// it is teleported onto the nearest ROAD surface, set upright, and turned to face
    /// the direction it was last travelling in.
    ///
    /// This replaces NWH's in-place "gradual flip over", which only rotated the vehicle
    /// where it lay (often still off the road, in a ditch, or upside down on terrain).
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

        [Tooltip("How far out to search for a road when the vehicle is off-road (metres).")]
        public float searchRadius = 80f;

        [Tooltip("Spacing of the search samples (metres). Smaller = more accurate, slower.")]
        public float searchStep = 3f;

        [Tooltip("How far above/below each sample point to cast when looking for road.")]
        public float rayUp = 30f;
        public float rayDown = 80f;

        [Header("Placement")]
        [Tooltip("Extra clearance above the road surface after placement (metres).")]
        public float clearance = 0.25f;

        [Tooltip("Face the direction the vehicle was last travelling. Off = keep current heading.")]
        public bool useTravelDirection = true;

        [Tooltip("Speed (m/s) above which the travel direction is remembered.")]
        public float travelDirMinSpeed = 1f;

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
        private float _bottomDrop = 1f;   // distance from pivot down to the lowest collider point
        private bool _prevPressed;
        private float _lastRecoverTime = -999f;

        // NWH flip module, so we can clear its "flippedOver" latch after we recover.
        private Component _flipModuleWrapper;
        private object _flipModule;
        private System.Reflection.FieldInfo _flippedOverField;

        private void Start()
        {
            _vc = GetComponent<VehicleController>();
            _rb = GetComponent<Rigidbody>();
            _lastTravelDir = transform.forward;

            // Distance from the pivot to the lowest point of the vehicle's colliders,
            // so we can drop it onto the road without sinking or hovering.
            Bounds b = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            if (any) _bottomDrop = Mathf.Max(0.1f, transform.position.y - b.min.y);

            CacheFlipModule();
        }

        private void CacheFlipModule()
        {
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "FlipOverModuleWrapper") continue;
                _flipModuleWrapper = mb;
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

        /// <summary>Teleport onto the nearest road, upright, facing the travel direction.</summary>
        public void RecoverToRoad()
        {
            RaycastHit hit;
            if (!TryFindRoad(transform.position, out hit))
            {
                Debug.LogWarning("[FlipOverToRoad] No road found within " + searchRadius +
                                 "m — uprighting in place instead.", this);
                UprightInPlace();
                ClearFlipLatch();
                return;
            }

            Vector3 up = hit.normal;

            // Heading: last travel direction (or current forward), flattened onto the road.
            Vector3 desired = useTravelDirection ? _lastTravelDir : transform.forward;
            Vector3 fwd = Vector3.ProjectOnPlane(desired, up);
            if (fwd.sqrMagnitude < 0.001f)
                fwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (fwd.sqrMagnitude < 0.001f)
                fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            fwd.Normalize();

            Vector3 pos = hit.point + up * (_bottomDrop + clearance);
            Quaternion rot = Quaternion.LookRotation(fwd, up);

            if (resetVelocity)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.position = pos;
            _rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);

            ClearFlipLatch();
        }

        private void UprightInPlace()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 pos = transform.position + Vector3.up * 0.5f;
            if (resetVelocity) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            _rb.position = pos; _rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
        }

        // Stops NWH's module from then running its own 3-second gradual flip on the
        // vehicle we just placed correctly.
        private void ClearFlipLatch()
        {
            if (_flipModule != null && _flippedOverField != null)
                _flippedOverField.SetValue(_flipModule, false);
        }

        // ── Road search ──────────────────────────────────────────────────────────

        /// <summary>Nearest road surface to <paramref name="origin"/>, searched in rings.</summary>
        private bool TryFindRoad(Vector3 origin, out RaycastHit best)
        {
            best = default(RaycastHit);

            // Directly beneath first.
            if (RaycastRoad(origin, out best)) return true;

            for (float r = searchStep; r <= searchRadius; r += searchStep)
            {
                int steps = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / searchStep));
                float bestSqr = float.MaxValue;
                bool found = false;
                RaycastHit ringBest = default(RaycastHit);

                for (int i = 0; i < steps; i++)
                {
                    float a = (i / (float)steps) * Mathf.PI * 2f;
                    Vector3 p = origin + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                    RaycastHit h;
                    if (!RaycastRoad(p, out h)) continue;

                    float d = (h.point - origin).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; ringBest = h; found = true; }
                }

                if (found) { best = ringBest; return true; }  // nearest ring wins
            }
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
                if (h.collider.transform.IsChildOf(transform)) continue; // never hit ourselves

                if (h.point.y > bestY) { bestY = h.point.y; hit = h; ok = true; }
            }
            return ok;
        }
    }
}

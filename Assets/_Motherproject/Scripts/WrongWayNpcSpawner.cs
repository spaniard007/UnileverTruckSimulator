using UnityEngine;
using UnityEngine.Splines; // Requires the "Splines" package in Unity
using NWH.VehiclePhysics2;

namespace BLI.CMP
{
    /// <summary>
    /// Spawns a "wrong-way" pedestrian NPC (e.g. a person walking / crossing into the
    /// road against the flow) when the player vehicle enters this trigger. The NPC
    /// follows a Unity Spline via SplineAnimate and is destroyed after a lifetime.
    ///
    /// If the player hits the spawned NPC it is tagged as a Human, so the
    /// <see cref="GameManager"/> automatically applies the Human collision penalty
    /// (default -50). No extra wiring needed — this mirrors how
    /// <see cref="WrongWaySpawner"/> works for wrong-way vehicles.
    ///
    /// "Sometimes an NPC goes the wrong way": each trigger rolls <see cref="wrongWayChance"/>
    /// to decide whether a wrong-way NPC actually spawns this time.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WrongWayNpcSpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [Tooltip("Pedestrian NPC prefabs to spawn from randomly (e.g. NWH 'Character DWP2'). " +
                 "They will be tagged 'Human' so a hit triggers the GameManager Human penalty.")]
        public GameObject[] npcPrefabs;

        [Tooltip("The Unity Spline path the NPC walks along (route it into the road / wrong way).")]
        public SplineContainer path;

        [Tooltip("When ON, the NPC despawns only when it reaches the END of its spline path " +
                 "(not on a fixed timer), so slow NPCs aren't destroyed mid-path.")]
        public bool despawnOnSplineEnd = true;

        [Tooltip("Fixed lifetime (seconds). Used as the despawn time when 'Despawn On Spline End' " +
                 "is OFF, and as a safety cap otherwise (auto-extended to cover the full path).")]
        public float lifetime = 30f;

        [Tooltip("Should this spawner only trigger once?")]
        public bool triggerOnlyOnce = true;

        [Header("Wrong-Way Chance")]
        [Tooltip("Probability (0..1) that entering this trigger actually spawns a wrong-way NPC. " +
                 "1 = always, 0.3 = roughly one in three, etc. This is the 'sometimes' behaviour.")]
        [Range(0f, 1f)]
        public float wrongWayChance = 0.5f;

        [Header("Movement Settings")]
        [Tooltip("Walking speed of the NPC along the spline (Unity units / second). ~1.4 = human walk, ~3 = jog.")]
        public float speed = 1.4f;

        [Tooltip("Make the NPC face its walking direction along the spline.")]
        public bool faceMoveDirection = true;

        [Header("Penalty / Tagging")]
        [Tooltip("Tag applied to the spawned NPC so GameManager classifies it as a Human. " +
                 "Must be one of GameManager.humanTags (default 'Human').")]
        public string npcTag = "Human";

        [Tooltip("Tag the whole spawned hierarchy (not just the root). Recommended for ragdoll " +
                 "characters whose limb colliders are the ones the vehicle actually hits.")]
        public bool tagWholeHierarchy = true;

        [Header("Physics Setup")]
        [Tooltip("Freeze the NPC's rigidbodies (make them kinematic, no gravity) so a ragdoll " +
                 "prefab rigidly follows the spline instead of collapsing. Collisions still register.")]
        public bool freezeRigidbodies = true;

        [Tooltip("If the prefab has NO collider at all, add a capsule so the player can hit it.")]
        public bool ensureCollider = true;

        [Tooltip("Make the NPC's colliders triggers so the car passes THROUGH the NPC instead " +
                 "of physically hitting it (stops the car flying off). The hit is still detected " +
                 "(death animation + penalty) via trigger overlap.")]
        public bool useTriggerCollider = true;

        private bool _hasTriggered = false;

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log(other.gameObject.name + "Car Entered");
            if (_hasTriggered && triggerOnlyOnce) return;

            // Only react to the player's vehicle (NWH colliders are children of the VehicleController).
            VehicleController vc = other.GetComponentInParent<VehicleController>();
            if (vc == null || !vc.isPlayerControllable) return;

            _hasTriggered = true; // consume the trigger even if the dice say "no NPC this time"

            // "Sometimes" the NPC goes the wrong way.
            //if (Random.value <= wrongWayChance)
            SpawnWrongWayNpc();
        }

        private void SpawnWrongWayNpc()
        {
            if (npcPrefabs == null || npcPrefabs.Length == 0 || path == null)
            {
                Debug.LogWarning("[WrongWayNpcSpawner] Missing NPC prefabs or spline path!", this);
                return;
            }

            GameObject prefab = npcPrefabs[Random.Range(0, npcPrefabs.Length)];
            GameObject npc = Instantiate(prefab, path.transform.position, path.transform.rotation);

            ApplyTag(npc);
            SetupPhysics(npc);
            SetupDeathReaction(npc);
            SetupSplineMovement(npc);
            SetupDespawn(npc);
        }

        // ── Tagging ──────────────────────────────────────────────────────────────
        private void ApplyTag(GameObject npc)
        {
            try
            {
                npc.tag = npcTag;
                if (tagWholeHierarchy)
                    foreach (Transform t in npc.GetComponentsInChildren<Transform>(true))
                        t.gameObject.tag = npcTag;
            }
            catch
            {
                Debug.LogWarning($"[WrongWayNpcSpawner] Tag '{npcTag}' does not exist in Project Settings > Tags. " +
                                 "Add it (and make sure it is listed in GameManager.humanTags) so the penalty applies.", this);
            }
        }

        // ── Physics ──────────────────────────────────────────────────────────────
        private void SetupPhysics(GameObject npc)
        {
            // Ragdoll / character prefabs carry many rigidbodies; make them kinematic so the
            // NPC follows the spline rigidly and doesn't fall apart. Kinematic bodies still
            // report collisions to the player's dynamic rigidbody, so the penalty fires.
            if (freezeRigidbodies)
            {
                foreach (var rb in npc.GetComponentsInChildren<Rigidbody>(true))
                {
                    rb.isKinematic = true;
                    rb.useGravity  = false;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                }
            }

            // Only add a collider if the prefab has none at all (don't duplicate ragdoll colliders).
            if (ensureCollider && npc.GetComponentInChildren<Collider>(true) == null)
            {
                var cap = npc.AddComponent<CapsuleCollider>();
                cap.height = 1.8f;
                cap.radius = 0.3f;
                cap.center = new Vector3(0f, 0.9f, 0f);
            }

            // A kinematic Rigidbody is required for the NPC to move cleanly along the spline
            // AND to receive trigger/collision callbacks (so the death reaction fires). Add one
            // if the prefab has none.
            if (npc.GetComponentInChildren<Rigidbody>(true) == null)
            {
                var rb = npc.AddComponent<Rigidbody>();
                rb.isKinematic   = true;
                rb.useGravity    = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            // Trigger colliders: the car drives through the NPC (no launch) but the overlap is
            // still detected. NpcDeathOnHit reacts via OnTriggerEnter and reports the penalty.
            if (useTriggerCollider)
                foreach (var col in npc.GetComponentsInChildren<Collider>(true))
                    col.isTrigger = true;
        }

        // ── Death reaction ───────────────────────────────────────────────────────
        private void SetupDeathReaction(GameObject npc)
        {
            // Adds the play-death-on-hit behaviour if the prefab doesn't already carry it.
            if (npc.GetComponentInChildren<NpcDeathOnHit>(true) == null)
                npc.AddComponent<NpcDeathOnHit>();
        }

        // ── Spline movement ──────────────────────────────────────────────────────
        private void SetupSplineMovement(GameObject npc)
        {
            SplineAnimate anim = npc.GetComponent<SplineAnimate>();
            if (anim == null) anim = npc.AddComponent<SplineAnimate>();

            anim.Container       = path;
            anim.AnimationMethod = SplineAnimate.Method.Speed;
            anim.MaxSpeed        = speed;
            anim.Loop            = SplineAnimate.LoopMode.Once;
            anim.Alignment       = faceMoveDirection
                ? SplineAnimate.AlignmentMode.SplineElement
                : SplineAnimate.AlignmentMode.None;
            anim.Play();
        }

        // ── Despawn ──────────────────────────────────────────────────────────────
        private void SetupDespawn(GameObject npc)
        {
            if (despawnOnSplineEnd)
            {
                var d = npc.GetComponent<DespawnAtSplineEnd>();
                if (d == null) d = npc.AddComponent<DespawnAtSplineEnd>();

                // Safety cap must exceed the real path duration so it never kills mid-path.
                float cap = lifetime;
                var anim = npc.GetComponent<SplineAnimate>();
                if (anim != null && anim.Duration > 0f)
                    cap = Mathf.Max(lifetime, anim.Duration + 5f);
                d.safetyLifetime = cap;
            }
            else
            {
                // Old behaviour: fixed timer.
                Destroy(npc, lifetime);
            }
        }

        // ── Editor visualization ─────────────────────────────────────────────────
        private void OnDrawGizmos()
        {
            // Yellow-ish for pedestrians (vs the red wrong-way vehicle spawner).
            Gizmos.color = new Color(1f, 0.85f, 0f, 0.3f);

            BoxCollider box = GetComponent<BoxCollider>();
            if (box != null)
            {
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else
            {
                Gizmos.DrawWireSphere(transform.position, transform.localScale.x);
            }
        }
    }
}

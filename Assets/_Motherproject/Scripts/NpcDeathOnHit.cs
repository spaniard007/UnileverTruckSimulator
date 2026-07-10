using UnityEngine;
using UnityEngine.Splines;
using NWH.VehiclePhysics2;

namespace BLI.CMP
{
    /// <summary>
    /// Plays a death animation when the player's vehicle hits this NPC, stops it from
    /// walking, and despawns it after a delay. Designed for the "NPC" Animator
    /// Controller whose death state ("Flying Back Death") has no trigger parameter, so
    /// it is played directly by state name.
    ///
    /// Works whether the NPC is dropped in the scene manually or spawned by
    /// <see cref="WrongWayNpcSpawner"/>. The GameManager still applies the Human penalty
    /// independently (via the player's collision + the 'Human' tag); this component only
    /// handles the visual death reaction.
    /// </summary>
    public class NpcDeathOnHit : MonoBehaviour
    {
        [Header("Animation")]
        [Tooltip("Animator that holds the death state. Auto-found in children if left empty.")]
        public Animator animator;

        [Tooltip("Exact name of the death state in the Animator Controller.")]
        public string deathStateName = "Flying Back Death";

        [Tooltip("Cross-fade time into the death state (seconds). 0 = snap instantly.")]
        public float crossFade = 0.05f;

        [Header("Detection")]
        [Tooltip("Also react to trigger overlaps, not just solid collisions.")]
        public bool detectTriggers = true;

        [Header("Penalty")]
        [Tooltip("Apply the GameManager Human penalty when the player hits this NPC. Needed " +
                 "because a trigger collider does NOT generate the physics collision the " +
                 "GameManager normally listens for, so the NPC reports the hit itself.")]
        public bool applyHumanPenalty = true;

        [Header("After Death")]
        [Tooltip("Stop advancing along the spline so the body doesn't keep sliding.")]
        public bool stopMovingOnDeath = true;

        [Tooltip("Destroy the NPC this many seconds after death. 0 = leave it (spawner lifetime still applies).")]
        public float destroyAfter = 4f;

        private bool _dead;

        private void Reset()
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);
        }

        private void OnCollisionEnter(Collision c)
        {
            TryDie(c.collider);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (detectTriggers) TryDie(other);
        }

        private void TryDie(Collider other)
        {
            if (_dead || other == null) return;

            // Only the player's vehicle kills the NPC.
            VehicleController vc = other.GetComponentInParent<VehicleController>();
            if (vc == null || !vc.isPlayerControllable) return;

            Die(true);
        }

        /// <summary>
        /// Force the death reaction. <paramref name="fromPlayerHit"/> also applies the
        /// GameManager penalty (used when the player is the cause).
        /// </summary>
        public void Die(bool fromPlayerHit = false)
        {
            if (_dead) return;
            _dead = true;

            // A trigger collider produces no physics collision, so the GameManager never
            // sees it — report the Human penalty here instead.
            if (fromPlayerHit && applyHumanPenalty && GameManager.Instance != null)
                GameManager.Instance.ApplyPenalty(GameManager.Instance.penaltyHuman,
                    $"Wrong-way NPC hit ({name})");

            if (stopMovingOnDeath)
            {
                // Disabling SplineAnimate freezes the transform where it was hit
                // (version-agnostic — no dependency on Pause()).
                var sa = GetComponent<SplineAnimate>();
                if (sa != null) sa.enabled = false;
            }

            if (animator != null && !string.IsNullOrEmpty(deathStateName))
            {
                if (crossFade > 0f)
                    animator.CrossFadeInFixedTime(deathStateName, crossFade, 0, 0f);
                else
                    animator.Play(deathStateName, 0, 0f);
            }
            else
            {
                Debug.LogWarning("[NpcDeathOnHit] No Animator or death state name set — cannot play death.", this);
            }

            if (destroyAfter > 0f)
                Destroy(gameObject, destroyAfter);
        }
    }
}

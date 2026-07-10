using UnityEngine;
using UnityEngine.Splines;

namespace BLI.CMP
{
    /// <summary>
    /// Destroys this GameObject when its <see cref="SplineAnimate"/> reaches the END of
    /// the spline, instead of on a fixed timer — so an NPC despawns at the finish of its
    /// path rather than disappearing mid-way. A safety lifetime still caps it in case the
    /// spline never completes (e.g. it was paused on death, or set to loop).
    /// </summary>
    [RequireComponent(typeof(SplineAnimate))]
    public class DespawnAtSplineEnd : MonoBehaviour
    {
        [Tooltip("Hard cap (seconds) after which the object despawns even if the spline never " +
                 "finishes. Acts as a safety net only. 0 = no cap (rely purely on completion).")]
        public float safetyLifetime = 30f;

        private SplineAnimate _anim;
        private float _age;
        private bool  _wasPlaying;
        private bool  _done;

        private void Awake()
        {
            _anim = GetComponent<SplineAnimate>();
        }

        private void OnEnable()
        {
            if (_anim != null) _anim.Completed += OnSplineCompleted;
        }

        private void OnDisable()
        {
            if (_anim != null) _anim.Completed -= OnSplineCompleted;
        }

        private void OnSplineCompleted() => Despawn();

        private void Update()
        {
            if (_done) return;

            _age += Time.deltaTime;

            // Fallback in case the Completed event doesn't fire: once it has actually
            // started moving and then reaches the very end, despawn.
            if (_anim != null)
            {
                if (_anim.IsPlaying)
                    _wasPlaying = true;
                else if (_wasPlaying && _anim.NormalizedTime >= 0.999f)
                {
                    Despawn();
                    return;
                }
            }

            // Last-resort safety cap so a stuck NPC never lives forever.
            if (safetyLifetime > 0f && _age >= safetyLifetime)
                Despawn();
        }

        private void Despawn()
        {
            if (_done) return;
            _done = true;
            Destroy(gameObject);
        }
    }
}

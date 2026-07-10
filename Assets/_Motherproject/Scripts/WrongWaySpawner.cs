using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines; // Requires the "Splines" package in Unity
using NWH.VehiclePhysics2;

namespace BLI.CMP
{
    /// <summary>
    /// Spawns a wrong-way vehicle (e.g., rickshaw, car) when the player enters this trigger.
    /// The spawned vehicle will follow a Unity Spline using SplineAnimate and will be destroyed after some time.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WrongWaySpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [Tooltip("The vehicle prefabs (e.g., Rickshaw, Car) to spawn from randomly. Ideally should have a Rigidbody/Collider and be tagged 'WrongWayVehicle'.")]
        public GameObject[] vehiclePrefabs;
        
        [Tooltip("The Unity Spline path for the vehicle to follow.")]
        public SplineContainer path;
        
        [Tooltip("When ON, the vehicle despawns only when it reaches the END of its spline path " +
                 "(not on a fixed timer), so slower vehicles aren't destroyed mid-path.")]
        public bool despawnOnSplineEnd = true;

        [Tooltip("Fixed lifetime (seconds). Used as the despawn time when 'Despawn On Spline End' " +
                 "is OFF, and as a safety cap otherwise (auto-extended to cover the full path).")]
        public float lifetime = 30f;

        [Tooltip("Should this spawner only trigger once?")]
        public bool triggerOnlyOnce = true;

        [Header("Movement Settings")]
        [Tooltip("Speed of the spawned vehicle along the spline (km/h or m/s depending on your SplineAnimate settings, usually Unity units/sec).")]
        public float speed = 10f;
        
        private bool _hasTriggered = false;

        private void OnTriggerEnter(Collider other)
        {
            if (_hasTriggered && triggerOnlyOnce) return;

            // Check if the collider belongs to the player's vehicle
            // NWH Vehicle colliders are usually children of the main VehicleController
            VehicleController vc = other.GetComponentInParent<VehicleController>();
            
            if (vc != null && vc.isPlayerControllable)
            {
                SpawnWrongWayVehicle();
                _hasTriggered = true;
            }
        }

        private void SpawnWrongWayVehicle()
        {
            if (vehiclePrefabs == null || vehiclePrefabs.Length == 0 || path == null)
            {
                Debug.LogWarning("[WrongWaySpawner] Missing prefabs or spline path!");
                return;
            }

            // Pick a random vehicle prefab
            GameObject selectedPrefab = vehiclePrefabs[Random.Range(0, vehiclePrefabs.Length)];

            // Spawn the vehicle
            GameObject spawnedVehicle = Instantiate(selectedPrefab, path.transform.position, path.transform.rotation);
            
            // Ensure the spawned vehicle has the WrongWayVehicle tag so the GameManager gives a -20 penalty if hit
            if (string.IsNullOrEmpty(spawnedVehicle.tag) || spawnedVehicle.tag == "Untagged")
            {
                try
                {
                    spawnedVehicle.tag = "WrongWayVehicle";
                }
                catch
                {
                    Debug.LogWarning("[WrongWaySpawner] 'WrongWayVehicle' tag does not exist in Project Settings. Please add it.");
                }
            }

            // Setup SplineAnimate
            SplineAnimate splineAnimate = spawnedVehicle.GetComponent<SplineAnimate>();
            if (splineAnimate == null)
            {
                splineAnimate = spawnedVehicle.AddComponent<SplineAnimate>();
            }

            splineAnimate.Container = path;
            splineAnimate.AnimationMethod = SplineAnimate.Method.Speed;
            splineAnimate.MaxSpeed = speed;
            splineAnimate.Loop = SplineAnimate.LoopMode.Once;
            splineAnimate.Play();

            // Despawn when the spline finishes (not on a fixed timer), so a slow vehicle
            // isn't destroyed mid-path. Falls back to a fixed lifetime if disabled.
            if (despawnOnSplineEnd)
            {
                var d = spawnedVehicle.GetComponent<DespawnAtSplineEnd>();
                if (d == null) d = spawnedVehicle.AddComponent<DespawnAtSplineEnd>();

                float cap = lifetime;
                if (splineAnimate.Duration > 0f)
                    cap = Mathf.Max(lifetime, splineAnimate.Duration + 5f);
                d.safetyLifetime = cap;
            }
            else
            {
                Destroy(spawnedVehicle, lifetime);
            }
        }
        
        // Editor visualization to make it easy to see the trigger zone in the Scene view
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            
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

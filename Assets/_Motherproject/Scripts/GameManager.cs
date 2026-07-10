using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2;

namespace BLI.CMP
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Enums & Data
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies objects the player vehicle can collide with.
    /// Apply these tags to scene objects via Edit > Project Settings > Tags.
    /// </summary>
    public enum CollisionCategory
    {
        Unknown,
        /// <summary>Trees, road dividers, bricks, etc.</summary>
        StaticObject,
        /// <summary>Normal traffic vehicles (Valo / NPC).</summary>
        TrafficVehicle,
        /// <summary>Wrong-way (ulta dike / oncoming) hostile vehicle.</summary>
        WrongWayVehicle,
        /// <summary>Human / pedestrian NPC.</summary>
        Human,
    }

    [Serializable]
    public class PenaltyEvent
    {
        public string reason;
        public int    penalty;
        public float  timeStamp;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GameManager
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Central game manager for the BLI CMP Driving Simulation.
    ///
    /// PENALTY TABLE (deducted from starting score of 100):
    ///  a. Static object collision (tree / divider / brick)  -10
    ///  b. Traffic vehicle (Valo vehicle) collision          -10
    ///  c. Wrong-way (ulta dike) vehicle collision           -20
    ///  d. Harsh acceleration (>= 2.5 m/s^2)                -10  (3 s cooldown)
    ///  e. Harsh braking    (<= -2.5 m/s^2)                 -10  (3 s cooldown)
    ///  f. Human / pedestrian collision                      -50
    ///  g. Overspeeding (> 60 km/h)                         -10  (5 s cooldown)
    ///
    /// SETUP:
    ///  1. Create a GameObject named "GameManager" and attach this script.
    ///  2. Drag your player VehicleController into the playerVehicle field,
    ///     OR leave it empty and it will be auto-detected at Start.
    ///  3. Tag your scene objects (Edit > Project Settings > Tags):
    ///       "StaticObject"    -> trees, dividers, bricks
    ///       "TrafficVehicle"  -> NPC / Valo vehicles
    ///       "WrongWayVehicle" -> oncoming / wrong-way vehicles
    ///       "Human"           -> pedestrians
    ///  4. Wire onScoreChanged / onPenaltyApplied to your HUD if desired.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────────

        [Header("Vehicle Reference")]
        [Tooltip("Assign the player VehicleController. Leave empty for auto-detection.")]
        public VehicleController playerVehicle;

        [Header("Score Settings")]
        [Tooltip("Starting score; penalties subtract from this.")]
        public int startingScore = 100;
        [Tooltip("Score floor - cannot go below this value.")]
        public int minimumScore  = 0;

        [Header("Collision Penalties")]
        [Tooltip("Static object (tree / divider / brick).")]
        public int penaltyStaticObject    = 10;
        [Tooltip("Normal traffic vehicle (Valo / NPC vehicle).")]
        public int penaltyTrafficVehicle  = 10;
        [Tooltip("Wrong-way (ulta dike / oncoming) vehicle.")]
        public int penaltyWrongWayVehicle = 20;
        [Tooltip("Human / pedestrian.")]
        public int penaltyHuman           = 50;

        [Header("Driving Behaviour - Thresholds")]
        [Tooltip("Positive forward acceleration (m/s^2) above which 'harsh acceleration' triggers.")]
        [Range(0f, 15f)]
        public float harshAccelThreshold  = 3.5f;
        [Tooltip("Negative forward acceleration (m/s^2) magnitude above which 'harsh braking' triggers.")]
        [Range(0f, 15f)]
        public float harshBrakeThreshold  = 4.5f;
        [Tooltip("Speed limit in km/h. Going faster triggers the overspeeding penalty.")]
        [Range(0f, 200f)]
        public float overspeedLimitKmh    = 60f;
        [Tooltip("How long (seconds) the acceleration/braking must be sustained to trigger a penalty. Filters out gear shifts.")]
        public float sustainedHarshTime   = 0.3f;

        [Header("Driving Behaviour - Penalties")]
        public int penaltyHarshAccel  = 10;
        public int penaltyHarshBrake  = 10;
        public int penaltyOverspeed   = 10;

        [Header("Cooldowns (seconds)")]
        [Tooltip("Seconds before another harsh-acceleration penalty can trigger.")]
        public float harshAccelCooldown = 3f;
        [Tooltip("Seconds before another harsh-braking penalty can trigger.")]
        public float harshBrakeCooldown = 3f;
        [Tooltip("Seconds between repeated overspeed penalties while still speeding.")]
        public float overspeedCooldown  = 5f;
        [Tooltip("Minimum impulse magnitude to register a collision penalty (filters road bumps).")]
        public float collisionImpulseThreshold = 1.0f;
        [Tooltip("Cooldown (s) before the same collider can trigger another collision penalty.")]
        public float collisionCooldown  = 1.5f;

        [Header("Tag Mapping")]
        [Tooltip("Unity tags classified as StaticObject.")]
        public List<string> staticObjectTags    = new List<string> { "StaticObject", "Tree", "Divider", "Brick" };
        [Tooltip("Unity tags classified as TrafficVehicle.")]
        public List<string> trafficVehicleTags  = new List<string> { "TrafficVehicle", "ValoVehicle", "NPC_Vehicle" };
        [Tooltip("Unity tags classified as WrongWayVehicle.")]
        public List<string> wrongWayVehicleTags = new List<string> { "WrongWayVehicle", "OpposeVehicle" };
        [Tooltip("Unity tags classified as Human / pedestrian.")]
        public List<string> humanTags           = new List<string> { "Human", "Pedestrian", "NPC_Human" };

        [Header("Events")]
        /// <summary>Fired whenever the score changes. Arg = new score value.</summary>
        public UnityEvent<int>          onScoreChanged  = new UnityEvent<int>();
        /// <summary>Fired whenever a penalty is applied.</summary>
        public UnityEvent<PenaltyEvent> onPenaltyApplied = new UnityEvent<PenaltyEvent>();

        // ── Runtime ────────────────────────────────────────────────────────────
        private int   _currentScore;
        private float _lastHarshAccelTime = -999f;
        private float _lastHarshBrakeTime = -999f;
        private float _lastOverspeedTime  = -999f;

        private float _sustainedAccelTime = 0f;
        private float _sustainedBrakeTime = 0f;
        private NWH.VehiclePhysics2.Damage.DamageHandler _damageHandler;

        private readonly Dictionary<int, float>  _collisionCooldownMap = new Dictionary<int, float>();
        private readonly List<PenaltyEvent>       _penaltyLog           = new List<PenaltyEvent>();

        // ── Public Accessors ───────────────────────────────────────────────────
        /// <summary>Current score, clamped to minimumScore.</summary>
        public int  CurrentScore   => _currentScore;
        /// <summary>All penalty events so far.</summary>
        public IReadOnlyList<PenaltyEvent> PenaltyLog => _penaltyLog;
        /// <summary>True when the player currently exceeds the speed limit.</summary>
        public bool IsOverspeeding => playerVehicle != null && playerVehicle.Speed * 3.6f > overspeedLimitKmh;
        /// <summary>Player's current speed in km/h.</summary>
        public float SpeedKmh      => playerVehicle != null ? playerVehicle.Speed * 3.6f : 0f;

        // ── Unity Lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _currentScore = startingScore;
        }

        private void Start()
        {
            if (playerVehicle == null)
            {
                Debug.LogWarning("[GameManager] playerVehicle not assigned — auto-detecting...");
                TryAutoDetectVehicle();
            }
            if (playerVehicle != null)
            {
                playerVehicle.onCollision.AddListener(OnVehicleCollision);
                _damageHandler = playerVehicle.GetComponent<NWH.VehiclePhysics2.Damage.DamageHandler>();
            }

            // Auto-add ScoreHUD if it doesn't exist so the user doesn't have to place it manually
            #if UNITY_2023_1_OR_NEWER
            if (FindObjectsByType<ScoreHUD>(FindObjectsSortMode.None).Length == 0)
            #else
            if (FindObjectOfType<ScoreHUD>() == null)
            #endif
            {
                gameObject.AddComponent<ScoreHUD>();
            }
        }

        private void Update()
        {
            if (playerVehicle == null || !playerVehicle.IsInitialized) return;
            CheckHarshDriving();
            CheckOverspeed();
        }

        private void OnDestroy()
        {
            if (playerVehicle != null)
                playerVehicle.onCollision.RemoveListener(OnVehicleCollision);
        }

        // ── Auto-Detect ────────────────────────────────────────────────────────

        private void TryAutoDetectVehicle()
        {
            foreach (var vc in FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
            {
                if (vc.isPlayerControllable)
                {
                    playerVehicle = vc;
                    Debug.Log($"[GameManager] Auto-detected: {vc.name}");
                    return;
                }
            }
            Debug.LogWarning("[GameManager] No player-controllable VehicleController found.");
        }

        // ── Collision Handling ─────────────────────────────────────────────────

        private void OnVehicleCollision(Collision collision)
        {
            // If NWH DamageHandler is present, use its native deceleration threshold instead of impulse
            if (_damageHandler != null && _damageHandler.enabled)
            {
                float accMag = collision.relativeVelocity.magnitude * 100f;
                if (accMag <= _damageHandler.decelerationThreshold) return; // Too weak to cause damage
            }
            else
            {
                if (collision.impulse.magnitude < collisionImpulseThreshold) return;
            }

            int id = collision.gameObject.GetInstanceID();
            if (_collisionCooldownMap.TryGetValue(id, out float t) && Time.time - t < collisionCooldown) return;
            _collisionCooldownMap[id] = Time.time;

            CollisionCategory cat = ClassifyCollider(collision.gameObject);

            switch (cat)
            {
                case CollisionCategory.StaticObject:
                    ApplyPenalty(penaltyStaticObject,    $"Static object collision ({collision.gameObject.name})");
                    break;
                case CollisionCategory.TrafficVehicle:
                    ApplyPenalty(penaltyTrafficVehicle,  $"Traffic vehicle collision ({collision.gameObject.name})");
                    break;
                case CollisionCategory.WrongWayVehicle:
                    ApplyPenalty(penaltyWrongWayVehicle, $"Wrong-way vehicle collision ({collision.gameObject.name})");
                    break;
                case CollisionCategory.Human:
                    ApplyPenalty(penaltyHuman,           $"Human collision ({collision.gameObject.name})");
                    break;
                default:
                    Debug.Log($"[GameManager] Unclassified collision: '{collision.gameObject.name}' tag='{collision.gameObject.tag}'");
                    break;
            }
        }

        // ── Tag Classification ─────────────────────────────────────────────────

        private CollisionCategory ClassifyCollider(GameObject go)
        {
            // Check self then walk hierarchy upward (for compound colliders)
            Transform t = go.transform;
            while (t != null)
            {
                string tag = t.tag;
                if (staticObjectTags.Contains(tag))    return CollisionCategory.StaticObject;
                if (trafficVehicleTags.Contains(tag))  return CollisionCategory.TrafficVehicle;
                if (wrongWayVehicleTags.Contains(tag)) return CollisionCategory.WrongWayVehicle;
                if (humanTags.Contains(tag))           return CollisionCategory.Human;
                t = t.parent;
            }
            return CollisionCategory.Unknown;
        }

        // ── Driving Behaviour ──────────────────────────────────────────────────

        private void CheckHarshDriving()
        {
            float accel = playerVehicle.LocalForwardAcceleration;

            if (accel >= harshAccelThreshold)
            {
                _sustainedAccelTime += Time.deltaTime;
                if (_sustainedAccelTime >= sustainedHarshTime && Time.time - _lastHarshAccelTime >= harshAccelCooldown)
                {
                    _lastHarshAccelTime = Time.time;
                    ApplyPenalty(penaltyHarshAccel, $"Harsh acceleration ({accel:F2} m/s^2)");
                }
            }
            else
            {
                _sustainedAccelTime = 0f;
            }

            if (accel <= -harshBrakeThreshold)
            {
                _sustainedBrakeTime += Time.deltaTime;
                if (_sustainedBrakeTime >= sustainedHarshTime && Time.time - _lastHarshBrakeTime >= harshBrakeCooldown)
                {
                    _lastHarshBrakeTime = Time.time;
                    ApplyPenalty(penaltyHarshBrake, $"Harsh braking ({accel:F2} m/s^2)");
                }
            }
            else
            {
                _sustainedBrakeTime = 0f;
            }
        }

        private void CheckOverspeed()
        {
            float kmh = playerVehicle.Speed * 3.6f;
            if (kmh > overspeedLimitKmh && Time.time - _lastOverspeedTime >= overspeedCooldown)
            {
                _lastOverspeedTime = Time.time;
                ApplyPenalty(penaltyOverspeed, $"Overspeeding ({kmh:F1} km/h)");
            }
        }

        // ── Score Management ───────────────────────────────────────────────────

        /// <summary>Deduct points and log the penalty.</summary>
        public void ApplyPenalty(int amount, string reason)
        {
            _currentScore = Mathf.Max(minimumScore, _currentScore - amount);

            var evt = new PenaltyEvent { reason = reason, penalty = amount, timeStamp = Time.time };
            _penaltyLog.Add(evt);

            Debug.Log($"[GameManager] PENALTY -{amount} | {reason} | Score={_currentScore}");
            onPenaltyApplied.Invoke(evt);
            onScoreChanged.Invoke(_currentScore);
        }

        /// <summary>Add bonus points.</summary>
        public void AddScore(int amount, string reason = "Bonus")
        {
            _currentScore += amount;
            Debug.Log($"[GameManager] BONUS +{amount} | {reason} | Score={_currentScore}");
            onScoreChanged.Invoke(_currentScore);
        }

        /// <summary>Reset score and all cooldown state.</summary>
        public void ResetScore()
        {
            _currentScore       = startingScore;
            _lastHarshAccelTime = -999f;
            _lastHarshBrakeTime = -999f;
            _lastOverspeedTime  = -999f;
            _penaltyLog.Clear();
            _collisionCooldownMap.Clear();
            onScoreChanged.Invoke(_currentScore);
            Debug.Log("[GameManager] Score reset.");
        }

        /// <summary>Hot-swap the tracked vehicle (e.g. after respawn).</summary>
        public void SetPlayerVehicle(VehicleController newVehicle)
        {
            if (playerVehicle != null)
                playerVehicle.onCollision.RemoveListener(OnVehicleCollision);
            
            playerVehicle = newVehicle;
            
            if (playerVehicle != null)
            {
                playerVehicle.onCollision.AddListener(OnVehicleCollision);
                _damageHandler = playerVehicle.GetComponent<NWH.VehiclePhysics2.Damage.DamageHandler>();
            }
        }
    }
}

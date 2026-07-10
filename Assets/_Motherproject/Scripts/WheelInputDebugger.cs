using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BLI.CMP
{
    /// <summary>
    /// Diagnostic logger for the steering-wheel / vehicle input. It prints, on a timer,
    /// the RAW connected joystick/gamepad axes, the NWH SteeringWheelInputProvider's
    /// mapped values, and the FINAL vehicle steering/throttle/brake — so you can see
    /// exactly which value is changing when the steering rotates on its own.
    ///
    /// Attach to any scene object, press Play with the wheel connected, and watch the
    /// Console. Remove/disable it once diagnosed.
    /// </summary>
    public class WheelInputDebugger : MonoBehaviour
    {
        [Tooltip("Seconds between console logs.")]
        public float logInterval = 0.25f;

        [Tooltip("Only print axis values whose magnitude exceeds this (reduces noise).")]
        public float axisThreshold = 0.02f;

        [Tooltip("List every connected input device once at start.")]
        public bool listDevicesOnStart = true;

        [Tooltip("Log even when everything is zero (so you can see it IS zero at rest).")]
        public bool logWhenIdle = true;

        private float _t;
        private MonoBehaviour _swProvider;
        private MonoBehaviour _vehicle;
        private PropertyInfo _steerP, _throttleP, _brakeP;
        private object _inputHandler;

        private void Start()
        {
            Resolve();
            if (listDevicesOnStart)
            {
                var sb = new StringBuilder("[WheelDebug] Connected input devices:\n");
                foreach (var d in InputSystem.devices)
                    sb.AppendLine("   - " + d.displayName + "  [" + d.GetType().Name + "]");
                Debug.Log(sb.ToString());
            }
        }

        // Find the provider + vehicle. Re-runs from Update until both are found, so it
        // still works if the vehicle is spawned/activated after this component starts.
        private void Resolve()
        {
            if (_swProvider == null || _vehicle == null)
            {
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (mb == null) continue;
                    string n = mb.GetType().Name;
                    if (_swProvider == null && n == "SteeringWheelInputProvider") _swProvider = mb;
                    if (_vehicle == null && n == "VehicleController") _vehicle = mb;
                }
            }

            if (_vehicle != null && _inputHandler == null)
            {
                var vt = _vehicle.GetType();
                var pi = vt.GetProperty("input");
                _inputHandler = pi != null ? pi.GetValue(_vehicle)
                                           : vt.GetField("input")?.GetValue(_vehicle);
                if (_inputHandler != null)
                {
                    var it = _inputHandler.GetType();
                    _steerP    = it.GetProperty("Steering");
                    _throttleP = it.GetProperty("Throttle");
                    _brakeP    = it.GetProperty("Brakes");
                    Debug.Log($"[WheelDebug] Resolved. SteeringWheelProvider={_swProvider != null}, Vehicle={_vehicle.name}");
                }
            }
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (_t < logInterval) return;
            _t = 0f;

            if (_swProvider == null || _inputHandler == null) Resolve();

            var sb = new StringBuilder("[WheelDebug] ");
            bool anyNonZero = false;

            // 1) RAW joystick axes (the actual wheel/pedal hardware).
            var js = Joystick.current;
            if (js != null)
            {
                sb.Append("RAW '" + js.displayName + "': ");
                foreach (var c in js.allControls)
                {
                    var ax = c as AxisControl;
                    if (ax == null) continue;
                    float v = 0f;
                    try { v = ax.ReadValue(); } catch { continue; }
                    if (Mathf.Abs(v) > axisThreshold)
                    {
                        sb.Append(ax.path.Replace(js.path, "") + "=" + v.ToString("F3") + " ");
                        anyNonZero = true;
                    }
                }
            }
            else sb.Append("RAW joystick=none ");

            var gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 ls = gp.leftStick.ReadValue();
                if (ls.sqrMagnitude > axisThreshold * axisThreshold) { sb.Append("| GP leftStick=" + ls + " "); anyNonZero = true; }
            }

            // 2) NWH SteeringWheelInputProvider mapped values (private fields).
            if (_swProvider != null)
            {
                var t = _swProvider.GetType();
                var bf = BindingFlags.NonPublic | BindingFlags.Instance;
                sb.Append("| SWmapped ");
                string[] fields = { "_steeringInput", "_throttleInput", "_brakeInput", "_clutchInput" };
                foreach (var fn in fields)
                {
                    var f = t.GetField(fn, bf);
                    if (f == null) continue;
                    object val = f.GetValue(_swProvider);
                    sb.Append(fn.Substring(1) + "=" + val + " ");
                    if (val is float fv && Mathf.Abs(fv) > axisThreshold) anyNonZero = true;
                }
            }

            // 3) FINAL vehicle input (what actually steers the truck).
            if (_inputHandler != null && _steerP != null)
            {
                float steer = (float)_steerP.GetValue(_inputHandler);
                float thr   = _throttleP != null ? (float)_throttleP.GetValue(_inputHandler) : 0f;
                float brk   = _brakeP != null ? (float)_brakeP.GetValue(_inputHandler) : 0f;
                sb.Append("| VEHICLE Steering=" + steer.ToString("F3") + " Throttle=" + thr.ToString("F2") + " Brakes=" + brk.ToString("F2"));
                if (Mathf.Abs(steer) > axisThreshold) anyNonZero = true;
            }

            if (anyNonZero || logWhenIdle)
                Debug.Log(sb.ToString());
        }
    }
}

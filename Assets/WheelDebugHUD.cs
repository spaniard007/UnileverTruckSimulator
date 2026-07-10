using UnityEngine;
using NWH.VehiclePhysics2;
using NWH.VehiclePhysics2.Input;

/// <summary>
/// Runtime on-screen overlay that shows whether the Logitech wheel (G29) is connected
/// and the live input state. Uses OnGUI so it ALSO works in a built player (not only the editor).
/// Toggle visibility with the toggle key (default F1).
/// </summary>
public class WheelDebugHUD : MonoBehaviour
{
    [Tooltip("Key to show/hide the overlay at runtime.")]
    public KeyCode toggleKey = KeyCode.F1;

    [Tooltip("Is the overlay visible on start?")]
    public bool visible = true;

    [Tooltip("Show the list of currently-held button indices (useful for shifter calibration).")]
    public bool showButtons = true;

    private SteeringWheelInputProvider _wheel;
    private GUIStyle _panel, _line, _head;
    private Texture2D _bg;
    private bool _init;

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) visible = !visible;
        if (_wheel == null) _wheel = FindObjectOfType<SteeringWheelInputProvider>();
    }

    private void InitStyles()
    {
        _bg = new Texture2D(1, 1);
        _bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.8f));
        _bg.Apply();

        _panel = new GUIStyle();
        _panel.normal.background = _bg;
        _panel.padding = new RectOffset(12, 12, 10, 10);

        int fs = Mathf.Max(13, Screen.height / 55);
        _line = new GUIStyle();
        _line.fontSize = fs;
        _line.richText = true;
        _line.normal.textColor = Color.white;
        _line.wordWrap = false;

        _head = new GUIStyle(_line);
        _head.fontSize = fs + 2;
        _head.fontStyle = FontStyle.Bold;

        _init = true;
    }

    private void OnGUI()
    {
        if (!visible) return;
        if (!_init) InitStyles();

        bool connected = _wheel != null && _wheel.deviceIndex >= 0;
        float w = Mathf.Clamp(Screen.width * 0.27f, 320f, 480f);
        float h = Mathf.Clamp(Screen.height * 0.34f, 200f, 360f);

        GUILayout.BeginArea(new Rect(12f, 12f, w, h), _panel);

        string col = connected ? "#41d156" : "#ff5a5a";
        string txt = connected ? "CONNECTED" : "NOT CONNECTED";
        GUILayout.Label("<color=" + col + ">●</color> STEERING WHEEL: <color=" + col + ">" + txt + "</color>", _head);
        GUILayout.Space(4f);

        if (_wheel == null)
        {
            GUILayout.Label("No SteeringWheelInputProvider in scene.", _line);
        }
        else
        {
            GUILayout.Label("Device : " + (string.IsNullOrEmpty(_wheel.foundDevicesDebugString) ? "-" : _wheel.foundDevicesDebugString), _line);
            GUILayout.Label("Index  : " + _wheel.deviceIndex + "    Vehicle: " + (_wheel.vehicleController != null ? _wheel.vehicleController.name : "none"), _line);
            GUILayout.Space(4f);

            GUILayout.Label("Steer  " + Bar(_wheel.Steering(), true) + "  " + _wheel.Steering().ToString("0.00"), _line);
            GUILayout.Label("Throt  " + Bar(_wheel.Throttle(), false) + "  " + _wheel.Throttle().ToString("0.00"), _line);
            GUILayout.Label("Brake  " + Bar(_wheel.Brakes(), false) + "  " + _wheel.Brakes().ToString("0.00"), _line);
            GUILayout.Label("Clutch " + Bar(_wheel.Clutch(), false) + "  " + _wheel.Clutch().ToString("0.00"), _line);

            if (_wheel.vehicleController != null)
            {
                int g = _wheel.vehicleController.powertrain.transmission.Gear;
                string gl = g < 0 ? "R" : (g == 0 ? "N" : g.ToString());
                GUILayout.Label("Gear   <color=#ffd84a>" + gl + "</color>    Speed " + (_wheel.vehicleController.Speed * 3.6f).ToString("0") + " km/h", _line);
            }

            if (showButtons && _wheel.buttonPressed != null)
            {
                string b = "";
                for (int i = 0; i < _wheel.buttonPressed.Length; i++)
                    if (_wheel.buttonPressed[i]) b += i + " ";
                GUILayout.Label("Buttons held: <color=#ffd84a>" + (b.Length == 0 ? "-" : b) + "</color>", _line);
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label("<color=#9aa0a6>[" + toggleKey + "] hide</color>", _line);
        GUILayout.EndArea();
    }

    private string GearOrNull() { return null; }

    private string Bar(float v, bool bipolar)
    {
        const int n = 14;
        var sb = new System.Text.StringBuilder("[");
        if (bipolar)
        {
            int pos = Mathf.Clamp(Mathf.RoundToInt((v * 0.5f + 0.5f) * (n - 1)), 0, n - 1);
            int mid = n / 2;
            for (int i = 0; i < n; i++) sb.Append(i == pos ? '|' : (i == mid ? ':' : '-'));
        }
        else
        {
            int fill = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v) * n), 0, n);
            for (int i = 0; i < n; i++) sb.Append(i < fill ? '=' : '-');
        }
        sb.Append("]");
        return sb.ToString();
    }
}

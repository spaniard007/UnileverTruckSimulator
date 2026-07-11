using UnityEngine;

/// <summary>
/// Simple performance overlay - shows FPS and related stats in the upper-right corner.
/// Just attach this script to any GameObject in your scene. No other setup needed.
/// </summary>
public class FPSMonitor : MonoBehaviour
{
    [Header("Display Settings")]
    [SerializeField] private int fontSize = 14;
    [SerializeField] private Color textColor = Color.green;
    [SerializeField] private float updateInterval = 0.5f; // how often the display refreshes (seconds)
    [SerializeField] private bool showMemory = true;
    [SerializeField] private bool showResolution = true;
    [SerializeField] private bool showDrawCallsHint = true;

    [Header("Toggle")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;
    [SerializeField] private bool startVisible = true;

    private float _timeAccum = 0f;
    private int _frames = 0;
    private float _fps = 0f;
    private float _ms = 0f;

    private float _minFps = float.MaxValue;
    private float _maxFps = 0f;

    private bool _visible = true;
    private GUIStyle _style;
    private Texture2D _bgTex;

    void Start()
    {
        _visible = startVisible;

        // Background texture for readability
        _bgTex = new Texture2D(1, 1);
        _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
        _bgTex.Apply();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            _visible = !_visible;

        _timeAccum += Time.unscaledDeltaTime;
        _frames++;

        if (_timeAccum >= updateInterval)
        {
            _fps = _frames / _timeAccum;
            _ms = (_timeAccum / _frames) * 1000f;

            if (_fps < _minFps) _minFps = _fps;
            if (_fps > _maxFps) _maxFps = _fps;

            _timeAccum = 0f;
            _frames = 0;
        }
    }

    void OnGUI()
    {
        if (!_visible) return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label);
            _style.fontSize = fontSize;
            _style.normal.textColor = textColor;
            _style.alignment = TextAnchor.UpperRight;
        }

        string text = BuildText();

        float boxWidth = 220f;
        float padding = 6f;
        int lineCount = text.Split('\n').Length;
        float lineHeight = fontSize + 4f;
        float boxHeight = lineCount * lineHeight + padding * 2f;

        float x = Screen.width - boxWidth - 10f;
        float y = 10f;

        // Background
        GUI.DrawTexture(new Rect(x, y, boxWidth, boxHeight), _bgTex);

        // Text
        GUI.Label(new Rect(x - padding, y + padding, boxWidth, boxHeight), text, _style);
    }

    private string BuildText()
    {
        Color fpsColor = _fps >= 50 ? Color.green : (_fps >= 30 ? Color.yellow : Color.red);
        _style.normal.textColor = fpsColor;

        string text = $"FPS: {_fps:F1} ({_ms:F1} ms)\n";
        text += $"Min/Max: {_minFps:F0} / {_maxFps:F0}\n";
        text += $"Target: {(Application.targetFrameRate < 0 ? "Unlimited" : Application.targetFrameRate.ToString())}\n";
        text += $"VSync: {QualitySettings.vSyncCount}\n";

        if (showMemory)
        {
            long allocatedMB = System.GC.GetTotalMemory(false) / (1024 * 1024);
            long reservedMB = (long)(UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024 * 1024));
            text += $"Mem: {allocatedMB}MB (Rsv: {reservedMB}MB)\n";
        }

        if (showResolution)
        {
            text += $"Res: {Screen.width}x{Screen.height}\n";
        }

        if (showDrawCallsHint)
        {
            // Note: real draw call / batch counts require the Profiler API (Editor-only / dev build)
            text += $"Quality: {QualitySettings.names[QualitySettings.GetQualityLevel()]}\n";
        }

        text += $"[{toggleKey}] toggle";

        return text;
    }

    void OnDestroy()
    {
        if (_bgTex != null)
            Destroy(_bgTex);
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BLI.CMP
{
    /// <summary>
    /// On-screen HUD for the driving score. The score + speed sit in a compact card at
    /// the TOP-CENTER of the screen. Whenever a penalty is applied a bold popup pops in
    /// at the CENTER of the screen (scale + fade), holds briefly, then fades and drifts
    /// up as it disappears. Built entirely from Unity UI (no TextMeshPro needed).
    /// </summary>
    [DisallowMultipleComponent]
    public class ScoreHUD : MonoBehaviour
    {
        [Header("Top-Center Card")]
        public float panelWidth  = 260f;
        public float panelHeight = 158f;
        [Tooltip("Distance (px) from the top edge of the screen.")]
        public float topMargin   = 16f;

        [Header("Score Colours")]
        public Color colorHigh = new Color(0.18f, 0.85f, 0.35f, 1f);
        public Color colorMid  = new Color(1.00f, 0.75f, 0.10f, 1f);
        public Color colorLow  = new Color(1.00f, 0.30f, 0.15f, 1f);

        [Header("Penalty Popup (center screen)")]
        [Tooltip("Font size of the big penalty number.")]
        public int   popupFontSize = 48;
        public Color penaltyPopupColor = new Color(1f, 0.28f, 0.24f, 1f);
        [Tooltip("Seconds the popup stays fully visible before it fades out.")]
        public float popupHoldTime = 1.0f;
        [Tooltip("Where the popup appears relative to screen centre (y>0 = above centre).")]
        public Vector2 popupCenterOffset = new Vector2(0f, 40f);

        [Header("Overspeeding Flash")]
        public Color overspeedColor = new Color(1f, 0.15f, 0.10f, 1f);
        public float flashRate      = 2f;

        private Canvas        _canvas;
        private Image         _panel;
        private Text          _scoreValue;
        private Text          _speedValue;
        private Text          _overspeedWarning;
        private RectTransform _popupContainer;
        private Font          _defaultFont;

        private int _activePopups;

        private void Awake()
        {
            _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildHUD();
        }

        private void Start()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.onScoreChanged.AddListener(RefreshScore);
            GameManager.Instance.onPenaltyApplied.AddListener(AddPenaltyPopup);
            RefreshScore(GameManager.Instance.CurrentScore);
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.onScoreChanged.RemoveListener(RefreshScore);
                GameManager.Instance.onPenaltyApplied.RemoveListener(AddPenaltyPopup);
            }
        }

        private void Update()
        {
            UpdateSpeedDisplay();
            UpdateOverspeedWarning();
        }

        // ── HUD construction ─────────────────────────────────────────────────────

        private void BuildHUD()
        {
            GameObject canvasGO = new GameObject("ScoreHUD_Canvas");
            canvasGO.transform.SetParent(transform);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGO.AddComponent<GraphicRaycaster>();

            BuildTopCard();
            BuildPopupContainer();
        }

        private void BuildTopCard()
        {
            GameObject panelGO = new GameObject("TopCard");
            panelGO.transform.SetParent(_canvas.transform, false);
            _panel = panelGO.AddComponent<Image>();
            _panel.color = new Color(0.05f, 0.05f, 0.10f, 0.82f);

            RectTransform panelRT = _panel.rectTransform;
            panelRT.anchorMin = new Vector2(0.5f, 1f);   // top-center
            panelRT.anchorMax = new Vector2(0.5f, 1f);
            panelRT.pivot     = new Vector2(0.5f, 1f);
            panelRT.sizeDelta = new Vector2(panelWidth, panelHeight);
            panelRT.anchoredPosition = new Vector2(0f, -topMargin);

            AddAccentLine(panelGO);

            Text scoreLabel = CreateText(panelGO, "ScoreLabel",
                new Vector2(0f, -14f), new Vector2(panelWidth - 24f, 26f),
                "SCORE", 16, TextAnchor.UpperCenter, new Color(0.7f, 0.7f, 0.7f));
            scoreLabel.fontStyle = FontStyle.Bold;

            _scoreValue = CreateText(panelGO, "ScoreValue",
                new Vector2(0f, -46f), new Vector2(panelWidth - 24f, 54f),
                "100", 48, TextAnchor.MiddleCenter, colorHigh);
            _scoreValue.fontStyle = FontStyle.Bold;

            CreateDivider(panelGO, new Vector2(0f, -102f), panelWidth - 32f);

            Text speedLabel = CreateText(panelGO, "SpeedLabel",
                new Vector2(-46f, -120f), new Vector2(80f, 24f),
                "", 12, TextAnchor.MiddleRight, new Color(0.65f, 0.65f, 0.65f));
            speedLabel.fontStyle = FontStyle.Bold;

            _speedValue = CreateText(panelGO, "SpeedValue",
                new Vector2(46f, -120f), new Vector2(130f, 24f),
                "0 km/h", 14, TextAnchor.MiddleLeft, Color.white);
            _speedValue.fontStyle = FontStyle.Bold;

            // Overspeed warning sits just BELOW the card so it doesn't crowd the number.
            _overspeedWarning = CreateText(panelGO, "OverspeedWarn",
                new Vector2(0f, -(panelHeight + 4f)), new Vector2(panelWidth + 60f, 24f),
                "! OVERSPEEDING !", 16, TextAnchor.MiddleCenter, overspeedColor);
            _overspeedWarning.fontStyle = FontStyle.Bold;
            _overspeedWarning.gameObject.SetActive(false);
        }

        private void BuildPopupContainer()
        {
            GameObject popGO = new GameObject("PenaltyPopups", typeof(RectTransform));
            popGO.transform.SetParent(_canvas.transform, false);
            _popupContainer = popGO.GetComponent<RectTransform>();
            _popupContainer.anchorMin = new Vector2(0.5f, 0.5f);   // screen center
            _popupContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _popupContainer.pivot     = new Vector2(0.5f, 0.5f);
            _popupContainer.anchoredPosition = popupCenterOffset;
            _popupContainer.sizeDelta = new Vector2(720f, 0f);
        }

        // ── Score / speed ────────────────────────────────────────────────────────

        private void RefreshScore(int newScore)
        {
            if (_scoreValue == null) return;
            _scoreValue.text = newScore.ToString();

            int startingScore = GameManager.Instance != null ? GameManager.Instance.startingScore : 100;
            float ratio = (float)newScore / Mathf.Max(1, startingScore);
            _scoreValue.color = ratio > 0.70f ? colorHigh : (ratio > 0.40f ? colorMid : colorLow);

            StopCoroutine(nameof(PulseScore));
            StartCoroutine(PulseScore());
        }

        private IEnumerator PulseScore()
        {
            float t = 0f;
            while (t < 0.25f)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Sin(t / 0.25f * Mathf.PI);
                _scoreValue.transform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 1.18f, p);
                yield return null;
            }
            _scoreValue.transform.localScale = Vector3.one;
        }

        private void UpdateSpeedDisplay()
        {
            if (_speedValue == null || GameManager.Instance == null) return;
            float kmh = GameManager.Instance.SpeedKmh;
            _speedValue.text = $"{kmh:F0} km/h";
            _speedValue.color = GameManager.Instance.IsOverspeeding ? overspeedColor : Color.white;
        }

        private void UpdateOverspeedWarning()
        {
            if (_overspeedWarning == null || GameManager.Instance == null) return;
            bool speeding = GameManager.Instance.IsOverspeeding;
            _overspeedWarning.gameObject.SetActive(speeding);
            if (speeding)
            {
                float a = Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * flashRate));
                Color c = overspeedColor;
                c.a = Mathf.Lerp(0.5f, 1f, a);
                _overspeedWarning.color = c;
            }
        }

        // ── Center penalty popup ───────────────────────────────────────────────────

        private void AddPenaltyPopup(PenaltyEvent evt)
        {
            if (_popupContainer == null) return;

            GameObject root = new GameObject("Penalty", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(_popupContainer, false);
            var rt = root.GetComponent<RectTransform>();
            var cg = root.GetComponent<CanvasGroup>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(720f, 96f);

            // Stack concurrent popups upward so they don't overlap.
            float baseY = _activePopups * 74f;
            rt.anchoredPosition = new Vector2(0f, baseY);
            cg.alpha = 0f;

            Text num = CreatePopupLine(root, $"-{evt.penalty}", popupFontSize, penaltyPopupColor,
                new Vector2(0f, 16f), new Vector2(720f, 60f));
            num.fontStyle = FontStyle.Bold;

            CreatePopupLine(root, ShortenReason(evt.reason), 22, new Color(1f, 0.86f, 0.82f, 1f),
                new Vector2(0f, -32f), new Vector2(720f, 28f));

            _activePopups++;
            StartCoroutine(AnimatePopup(rt, cg, baseY));
        }

        private IEnumerator AnimatePopup(RectTransform rt, CanvasGroup cg, float baseY)
        {
            // Pop in (scale up + fade in, ease-out).
            float t = 0f, inT = 0.22f;
            while (t < inT)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / inT);
                float e = 1f - Mathf.Pow(1f - p, 3f);
                rt.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.12f, e);
                cg.alpha = p;
                yield return null;
            }

            // Settle back to 1.0.
            t = 0f; float settle = 0.10f;
            while (t < settle)
            {
                t += Time.unscaledDeltaTime;
                rt.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, t / settle);
                yield return null;
            }
            rt.localScale = Vector3.one;
            cg.alpha = 1f;

            // Hold fully visible.
            float h = 0f;
            while (h < popupHoldTime) { h += Time.unscaledDeltaTime; yield return null; }

            // Fade out while drifting up.
            t = 0f; float outT = 0.45f;
            while (t < outT)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / outT);
                cg.alpha = 1f - p;
                rt.anchoredPosition = new Vector2(0f, baseY + p * 60f);
                yield return null;
            }

            _activePopups = Mathf.Max(0, _activePopups - 1);
            Destroy(rt.gameObject);
        }

        // ── UI helpers ─────────────────────────────────────────────────────────────

        private void AddAccentLine(GameObject parent)
        {
            GameObject line = new GameObject("AccentLine");
            line.transform.SetParent(parent.transform, false);
            Image img = line.AddComponent<Image>();
            img.color = new Color(0.28f, 0.68f, 1f, 0.9f);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 3f);
        }

        private void CreateDivider(GameObject parent, Vector2 anchoredPos, float width)
        {
            GameObject go = new GameObject("Divider");
            go.transform.SetParent(parent.transform, false);
            Image img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.08f);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = new Vector2(width, 1f);
        }

        private Text CreateText(GameObject parent, string goName, Vector2 anchoredPos, Vector2 sizeDelta,
            string defaultText, int fontSize, TextAnchor alignment, Color color)
        {
            GameObject go = new GameObject(goName);
            go.transform.SetParent(parent.transform, false);
            Text txt = go.AddComponent<Text>();
            txt.font = _defaultFont;
            txt.text = defaultText;
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.color = color;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform rt = txt.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return txt;
        }

        // Centered text used inside a penalty popup, with an outline for legibility over the road.
        private Text CreatePopupLine(GameObject parent, string s, int size, Color color,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject("Line");
            go.transform.SetParent(parent.transform, false);
            Text txt = go.AddComponent<Text>();
            txt.font = _defaultFont;
            txt.text = s;
            txt.fontSize = size;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform rt = txt.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            Outline ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(0f, 0f, 0f, 0.85f);
            ol.effectDistance = new Vector2(2f, -2f);
            return txt;
        }

        private string ShortenReason(string reason)
        {
            int paren = reason.IndexOf('(');
            if (paren > 0) reason = reason.Substring(0, paren).Trim();
            return reason.Length > 34 ? reason.Substring(0, 34) + "…" : reason;
        }
    }
}

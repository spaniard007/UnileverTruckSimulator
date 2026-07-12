using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to any Button GameObject.
/// Plays a hover sound on PointerEnter and a click sound on PointerClick.
/// </summary>
[RequireComponent(typeof(Button))]
public class ButtonSoundPlayer : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    [Header("Audio Source")]
    [Tooltip("AudioSource used to play sounds. If left empty, one is added automatically.")]
    public AudioSource audioSource;

    [Header("Sounds")]
    [Tooltip("Played when the pointer hovers over the button.")]
    public AudioClip hoverSound;

    [Tooltip("Played when the button is clicked.")]
    public AudioClip clickSound;

    [Header("Volume")]
    [Range(0f, 1f)] public float hoverVolume = 1f;
    [Range(0f, 1f)] public float clickVolume = 1f;

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Play(hoverSound, hoverVolume);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Play(clickSound, clickVolume);
    }

    private void Play(AudioClip clip, float volume)
    {
        if (audioSource == null || clip == null) return;
        audioSource.PlayOneShot(clip, volume);
    }
}
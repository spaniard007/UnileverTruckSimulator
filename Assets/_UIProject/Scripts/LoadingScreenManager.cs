using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class LoadingScreenManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject mainMenuObject;
    public GameObject loadingObject;
    public Slider progressSlider;

    [Header("Scene To Load")]
    public int gameSceneIndex = 1;

    void Start()
    {
        // Make sure loading screen is hidden at the start
        loadingObject.SetActive(false);
        mainMenuObject.SetActive(true);
    }
    public void OnPlayButtonPressed()
    {
        StartCoroutine(LoadGameSceneAsync());
    }

    public void OnQuitButtonPressed()
    {
        Application.Quit();
    }
    IEnumerator LoadGameSceneAsync()
    {
        // 1. Switch UI first
        mainMenuObject.SetActive(false);
        loadingObject.SetActive(true);
        progressSlider.value = 0f;

        // 2. Let Unity actually render the loading screen before we block the thread
        yield return null;
        yield return new WaitForEndOfFrame();

        // 3. Now start the load
        AsyncOperation operation = SceneManager.LoadSceneAsync(gameSceneIndex);
        operation.allowSceneActivation = false;

        while (!operation.isDone)
        {
            float displayProgress = Mathf.Clamp01(operation.progress / 0.9f);
            progressSlider.value = displayProgress;

            if (operation.progress >= 0.9f)
            {
                progressSlider.value = 1f;
                yield return new WaitForSeconds(0.25f);
                operation.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}
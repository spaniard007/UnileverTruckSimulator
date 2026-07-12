using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoaderOnHotkey : MonoBehaviour
{
    void Update()
    {
        bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (ctrlHeld && Input.GetKeyDown(KeyCode.Space))
        {
            SceneManager.LoadScene(1);
        }
        else if (ctrlHeld && Input.GetKeyDown(KeyCode.H))
        {
            SceneManager.LoadScene(0);
        }
    }
}
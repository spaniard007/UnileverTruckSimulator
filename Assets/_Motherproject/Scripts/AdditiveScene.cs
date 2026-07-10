using UnityEngine;
using UnityEngine.SceneManagement;

public class AdditiveScene : MonoBehaviour
{
   

void Start()
{
    SceneManager.LoadScene("main", LoadSceneMode.Additive);
}
// Update is called once per frame
void Update()
    {
        
    }
}

using UnityEngine;

public class FindObj : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Type the exact name of the layer you want to find.")]
    public string layerName;

    void Start()
    {
        // Convert the string layer name to its corresponding layer mask integer
        int targetLayer = LayerMask.NameToLayer(layerName);

        // Check if the layer actually exists in the project
        if (targetLayer == -1)
        {
            Debug.LogError($"[FindObjectsByLayer] The layer '{layerName}' does not exist in your Project Settings.");
            return;
        }

        // Find all GameObjects currently active in the scene
        GameObject[] allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        int matchCount = 0;

        Debug.Log($"--- Searching for GameObjects on layer: {layerName} ---");

        foreach (GameObject obj in allObjects)
        {
            if (obj.layer == targetLayer)
            {
                // Prints the object name and its hierarchy path so you can find it easily
                Debug.Log($"Found: '{obj.name}'", obj);
                matchCount++;
            }
        }

        Debug.Log($"--- Search finished. Found {matchCount} GameObject(s) on layer '{layerName}'. ---");
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class HologramSceneManager : MonoBehaviour
{
    private const string ReturnSceneName = "Main";

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.enterKey.wasPressedThisFrame ||
            keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            SceneManager.LoadScene(ReturnSceneName);
        }
    }
}

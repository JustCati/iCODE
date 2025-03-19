using UnityEngine;

public class ThreeDotMenu : MonoBehaviour
{
    [Header("Assign the panel you want to show/hide")]
    public GameObject canvasPanelVariant;

    private bool isPanelVisible = true;

    public void ToggleMenu()
    {
        isPanelVisible = !isPanelVisible;

        if (isPanelVisible)
            canvasPanelVariant.SetActive(true);
        else
            canvasPanelVariant.SetActive(false);
    }
}

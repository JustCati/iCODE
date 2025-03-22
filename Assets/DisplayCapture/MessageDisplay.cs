using UnityEngine;
using TMPro;

public class MessageDisplay : MonoBehaviour
{
    public TextMeshProUGUI messageText;

    public void ShowMessage(string msg){
        messageText.text = msg;
    }
}
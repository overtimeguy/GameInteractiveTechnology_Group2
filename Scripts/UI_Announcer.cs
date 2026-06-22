using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

public class UI_Announcer : UI_FadePanel
{
    [SerializeField] private TMP_Text tmpText;

    public string CurrentMessage { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        CacheText();
    }

    [Button]
    public void Activate(string message)
    {
        CurrentMessage = message;
        ApplyText(message);
        ActivatePanel();
    }

    [Button]
    public void Deactivate()
    {
        DeactivatePanel();
    }

    private void ApplyText(string message)
    {
        CacheText();

        if (tmpText != null)
        {
            tmpText.text = message;
        }
    }

    private void CacheText()
    {
        if (tmpText == null)
        {
            tmpText = GetComponentInChildren<TMP_Text>(true);
        }
    }
}

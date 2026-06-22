using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

public class UI_Warning : UI_FadePanel
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
        GlobalManager.SetBeautifyBlurRequest(this, true);
        ActivatePanel();
    }

    [Button]
    public void Deactivate()
    {
        GlobalManager.SetBeautifyBlurRequest(this, false);
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

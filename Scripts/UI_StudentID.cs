using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class UI_StudentID : UI_FadePanel
{
    private enum XKeyBehavior
    {
        Backspace,
        InsertX
    }

    [Serializable]
    private sealed class KeyBinding
    {
        [SerializeField] private VRButton button;
        [SerializeField] private string value;

        public VRButton Button => button;
        public string Value
        {
            get => value;
            set => this.value = value;
        }

        public KeyBinding(VRButton button, string value)
        {
            this.button = button;
            this.value = value;
        }
    }

    private readonly struct RuntimeKeyListener
    {
        public readonly VRButton Button;
        public readonly UnityAction Action;

        public RuntimeKeyListener(VRButton button, UnityAction action)
        {
            Button = button;
            Action = action;
        }
    }

    [Header("Student ID Input")]
    [SerializeField] private TMP_Text inputText;
    [SerializeField] private List<KeyBinding> keyBindings = new List<KeyBinding>();
    [SerializeField] private bool clearOnActivate = true;
    [SerializeField, Min(0)] private int maxInputLength;
    [SerializeField] private XKeyBehavior xKeyBehavior = XKeyBehavior.Backspace;

    private readonly List<RuntimeKeyListener> runtimeKeyListeners = new List<RuntimeKeyListener>();
    private string currentInput = string.Empty;
    private int savedInputValue;
    private bool hasSavedInputValue;
    private bool initialized;
    private bool missingInputWarningShown;

    public string CurrentInput => currentInput;
    public int SavedInputValue => savedInputValue;
    public bool HasSavedInputValue => hasSavedInputValue;
    public bool CanDeactivate => IsInputLengthComplete();

    protected override void Awake()
    {
        base.Awake();
        EnsureInitialized();
    }

    private void OnDestroy()
    {
        UnsubscribeKeyboard();
    }

    [Button]
    public void Activate()
    {
        EnsureInitialized();
        if (clearOnActivate)
        {
            ClearInput();
        }

        GlobalManager.SetBeautifyBlurRequest(this, true);
        ActivatePanel();
    }

    [Button]
    public void Deactivate()
    {
        if (!CanDeactivate)
        {
            Debug.LogWarning("[UI_StudentID] Cannot deactivate before input reaches max length.", this);
            return;
        }

        SaveCurrentInputValue();
        GlobalManager.SetBeautifyBlurRequest(this, false);
        DeactivatePanel();
    }

    public void InputKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        string normalizedKey = NormalizeKey(key);
        if (IsBackspaceKey(normalizedKey))
        {
            if (xKeyBehavior == XKeyBehavior.Backspace)
            {
                Backspace();
                return;
            }

            normalizedKey = "X";
        }

        if (!IsDigitKey(normalizedKey) && normalizedKey != "X")
        {
            return;
        }

        if (maxInputLength > 0 && currentInput.Length >= maxInputLength)
        {
            return;
        }

        currentInput += normalizedKey;
        RefreshInputText();
    }

    [Button]
    public void Backspace()
    {
        if (string.IsNullOrEmpty(currentInput))
        {
            return;
        }

        currentInput = currentInput.Substring(0, currentInput.Length - 1);
        RefreshInputText();
    }

    [Button]
    public void ClearInput()
    {
        currentInput = string.Empty;
        RefreshInputText();
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        CacheInputText();
        CacheKeyBindings();
        SubscribeKeyboard();
        RefreshInputText();
        initialized = true;
    }

    private void CacheInputText()
    {
        if (inputText != null)
        {
            currentInput = inputText.text ?? string.Empty;
            return;
        }

        Transform textField = FindChildByName(transform, "TEXT FIELD");
        if (textField != null)
        {
            inputText = textField.GetComponentInChildren<TMP_Text>(true);
        }

        if (inputText != null)
        {
            currentInput = inputText.text ?? string.Empty;
        }
    }

    private void CacheKeyBindings()
    {
        if (keyBindings == null)
        {
            keyBindings = new List<KeyBinding>();
        }

        if (keyBindings.Count == 0)
        {
            CacheKeyBindingsFromButtons();
        }

        if (keyBindings.Count == 0)
        {
            CacheKeyBindingsFromLabels();
        }

        for (int i = keyBindings.Count - 1; i >= 0; i--)
        {
            KeyBinding binding = keyBindings[i];
            if (binding == null || binding.Button == null)
            {
                keyBindings.RemoveAt(i);
                continue;
            }

            if (string.IsNullOrWhiteSpace(binding.Value))
            {
                binding.Value = GetButtonLabel(binding.Button);
            }
        }
    }

    private void CacheKeyBindingsFromButtons()
    {
        VRButton[] buttons = GetComponentsInChildren<VRButton>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            string label = GetButtonLabel(buttons[i]);
            if (IsSupportedKey(label))
            {
                keyBindings.Add(new KeyBinding(buttons[i], label));
            }
        }
    }

    private void CacheKeyBindingsFromLabels()
    {
        TMP_Text[] labels = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            TMP_Text label = labels[i];
            string key = NormalizeKey(label.text);
            if (!IsSupportedKey(key))
            {
                continue;
            }

            VRButton button = label.GetComponentInParent<VRButton>();
            if (button == null && label.transform.parent != null)
            {
                button = label.transform.parent.GetComponent<VRButton>();
                if (button == null)
                {
                    button = label.transform.parent.gameObject.AddComponent<VRButton>();
                }
            }

            if (button != null)
            {
                keyBindings.Add(new KeyBinding(button, key));
            }
        }
    }

    private void SubscribeKeyboard()
    {
        UnsubscribeKeyboard();

        for (int i = 0; i < keyBindings.Count; i++)
        {
            KeyBinding binding = keyBindings[i];
            if (binding == null || binding.Button == null || !IsSupportedKey(binding.Value))
            {
                continue;
            }

            string key = NormalizeKey(binding.Value);
            UnityAction action = () => InputKey(key);
            binding.Button.Clicked.AddListener(action);
            runtimeKeyListeners.Add(new RuntimeKeyListener(binding.Button, action));
        }
    }

    private void UnsubscribeKeyboard()
    {
        for (int i = 0; i < runtimeKeyListeners.Count; i++)
        {
            RuntimeKeyListener listener = runtimeKeyListeners[i];
            if (listener.Button != null && listener.Action != null)
            {
                listener.Button.Clicked.RemoveListener(listener.Action);
            }
        }

        runtimeKeyListeners.Clear();
    }

    private void RefreshInputText()
    {
        if (inputText == null)
        {
            if (!missingInputWarningShown)
            {
                missingInputWarningShown = true;
                Debug.LogWarning("[UI_StudentID] Input TMP_Text is not assigned.", this);
            }

            return;
        }

        inputText.text = currentInput;
    }

    private void SaveCurrentInputValue()
    {
        hasSavedInputValue = int.TryParse(currentInput, out savedInputValue);
        if (!hasSavedInputValue)
        {
            savedInputValue = 0;
        }
    }

    private bool IsInputLengthComplete()
    {
        return maxInputLength <= 0 || currentInput.Length == maxInputLength;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == childName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByName(root.GetChild(i), childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static string GetButtonLabel(VRButton button)
    {
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        return label != null ? NormalizeKey(label.text) : string.Empty;
    }

    private static bool IsSupportedKey(string key)
    {
        string normalizedKey = NormalizeKey(key);
        return IsDigitKey(normalizedKey) || IsBackspaceKey(normalizedKey);
    }

    private static bool IsDigitKey(string key)
    {
        return key != null && key.Length == 1 && key[0] >= '0' && key[0] <= '9';
    }

    private static bool IsBackspaceKey(string key)
    {
        return key == "X" || key == "DELETE" || key == "DEL" || key == "BACKSPACE";
    }

    private static string NormalizeKey(string key)
    {
        return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim().ToUpperInvariant();
    }
}

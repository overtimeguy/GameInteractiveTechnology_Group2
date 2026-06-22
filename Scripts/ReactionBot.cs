using UnityEngine;

[DisallowMultipleComponent]
public class ReactionBot : MonoBehaviour
{
    [SerializeField] private Animator animator;

    private void Start()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
    }
}

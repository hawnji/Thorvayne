using UnityEngine;

// Plays the character's animation clips (Idle, Walk, Run, Jump, Swim, Fly, Chop) to match what ThorvaynePlayer is doing.
// The Animator Controller just holds one state per clip; this script cross-fades between them.
public class ThorvayneCharacterAnimator : MonoBehaviour
{
    public ThorvaynePlayer player;
    public Animator animator;
    public ThorvayneWoodcutting woodcutting;
    public float walkAnimSpeed = 4f;     // ground speed (m/s) at which the walk clip plays at normal speed
    public float runAnimSpeed = 9f;      // same for the run clip
    public float runThreshold = 7.5f;    // faster than this = run
    public float fadeTime = 0.18f;
    [Tooltip("Nudges the model up (+) or down (-) in metres if its feet don't quite touch the ground.")]
    public float footOffset = 0f;

    string current = "";
    float airTime;
    bool hasChop;

    void Start()
    {
        if (player == null) player = GetComponentInParent<ThorvaynePlayer>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (woodcutting == null) woodcutting = GetComponentInParent<ThorvayneWoodcutting>();
        if (animator != null && footOffset != 0f) animator.transform.localPosition += Vector3.up * footOffset;
        hasChop = animator != null && animator.HasState(0, Animator.StringToHash("Chop"));
    }

    void Update()
    {
        if (player == null || animator == null) return;
        if (woodcutting == null) woodcutting = GetComponentInParent<ThorvayneWoodcutting>();   // added at runtime by ThorvaynePlayer
        float speed = player.GroundSpeed;
        airTime = player.IsGrounded ? 0f : airTime + Time.deltaTime;

        string state; float playSpeed = 1f;
        if (woodcutting != null && woodcutting.IsChopping) state = hasChop ? "Chop" : "Idle";
        else if (player.IsFlying) state = "Fly";
        else if (player.IsSwimming) { state = "Swim"; playSpeed = speed > 0.5f ? 1.3f : 0.8f; }
        else if (airTime > 0.15f) state = "Jump";
        else if (speed > runThreshold) { state = "Run"; playSpeed = Mathf.Clamp(speed / runAnimSpeed, 0.8f, 1.6f); }
        else if (speed > 0.3f) { state = "Walk"; playSpeed = Mathf.Clamp(speed / walkAnimSpeed, 0.6f, 1.8f); }
        else state = "Idle";

        if (state != current)
        {
            animator.CrossFadeInFixedTime(state, fadeTime);
            current = state;
        }
        animator.speed = playSpeed;
    }
}

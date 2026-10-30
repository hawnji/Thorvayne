using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Key presses for the gameplay scripts, with both the new Input System and the old Input Manager.
public static class ThorvayneKeys
{
#if ENABLE_INPUT_SYSTEM
    static Key ToKey(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.Tab: return Key.Tab;
            case KeyCode.Escape: return Key.Escape;
            case KeyCode.Return: return Key.Enter;
            default:
                if (k >= KeyCode.A && k <= KeyCode.Z) return Key.A + (k - KeyCode.A);
                if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return k == KeyCode.Alpha0 ? Key.Digit0 : Key.Digit1 + (k - KeyCode.Alpha1);
                return Key.None;
        }
    }
    public static bool Down(KeyCode k)
    {
        var kb = Keyboard.current; var key = ToKey(k);
        return kb != null && key != Key.None && kb[key].wasPressedThisFrame;
    }
#else
    public static bool Down(KeyCode k) { return Input.GetKeyDown(k); }
#endif
}

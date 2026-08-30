using TMPro;
using UnityEngine;

namespace VibeGame1
{
    public class PromptView : MonoBehaviour
    {
        public TMP_Text text;
        float alpha;
        string current = "";

        void OnEnable() { GameEvents.PromptChanged += Set; }
        void OnDisable() { GameEvents.PromptChanged -= Set; }

        public void Set(string s)
        {
            current = s ?? "";
            if (text != null && current.Length > 0) text.text = current;
        }

        void Update()
        {
            if (text == null) return;
            float target = current.Length > 0 ? 1f : 0f;
            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime * 8f);
            float pulse = current.Length > 0 ? 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 10f) : 1f;
            var c = text.color; c.a = alpha * pulse; text.color = c;
            text.transform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * 10f)) * Mathf.Lerp(0.8f, 1f, alpha);
        }
    }
}

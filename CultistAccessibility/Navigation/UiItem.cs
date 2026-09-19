using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CultistAccessibility.Navigation
{
    internal enum UiItemKind
    {
        Button,
        Toggle,
        Slider,
        Dropdown,
        InputField,
        Text,
        Custom
    }

    /// <summary>One navigable thing on a uGUI screen: a Selectable or a read-only text row.</summary>
    internal sealed class UiItem
    {
        public GameObject Go;
        public Selectable Selectable;
        public TMP_Text Text;
        public UiItemKind Kind;
        public RectTransform Rect;
        public Canvas Canvas;
        /// <summary>The panel the item belongs to: nearest CanvasGroup, or the top-level child of its canvas.</summary>
        public Transform Window;
        public ScrollRect Scroll;
        public Vector2 ScreenPos;
        public int Order;

        /// <summary>For Custom items: label and activation supplied by code.</summary>
        public System.Func<string> CustomLabel;
        public System.Action CustomActivate;

        public bool IsValid => Go != null && Go.activeInHierarchy;

        public bool Interactable => Selectable == null || Selectable.IsInteractable();
    }
}

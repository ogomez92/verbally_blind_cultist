using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CultistAccessibility.Navigation
{
    /// <summary>
    /// Mouse emulation for uGUI objects: hover (pointer enter/exit), click, scrolling into view.
    /// uGUI selection is kept empty so the input module's Submit cannot re-click an old button.
    /// </summary>
    internal static class UiActions
    {
        private static GameObject _hovered;

        public static PointerEventData MakePointer(GameObject target)
        {
            var es = EventSystem.current;
            var ped = new PointerEventData(es)
            {
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                clickTime = Time.unscaledTime
            };
            if (target != null)
            {
                var rt = target.transform as RectTransform;
                if (rt != null)
                {
                    var canvas = target.GetComponentInParent<Canvas>();
                    Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    Vector3 world = rt.TransformPoint(rt.rect.center);
                    ped.position = RectTransformUtility.WorldToScreenPoint(cam, world);
                    ped.pressPosition = ped.position;
                }
                ped.pointerPress = target;
                ped.rawPointerPress = target;
                ped.pointerEnter = target;
            }
            return ped;
        }

        /// <summary>Moves the emulated hover: exit the old object first, then enter the new one.</summary>
        public static void Hover(GameObject target)
        {
            if (_hovered == target) return;
            if (_hovered != null)
            {
                try { ExecuteEvents.ExecuteHierarchy(_hovered, MakePointer(_hovered), ExecuteEvents.pointerExitHandler); } catch { }
            }
            _hovered = target;
            if (target != null)
            {
                try { ExecuteEvents.ExecuteHierarchy(target, MakePointer(target), ExecuteEvents.pointerEnterHandler); } catch { }
            }
        }

        public static void ClearHover() => Hover(null);

        /// <summary>Full click: down, up, click. Returns true when some handler received the click.</summary>
        public static bool Click(GameObject target)
        {
            if (target == null) return false;
            var ped = MakePointer(target);
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerUpHandler);
            GameObject handled = ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerClickHandler);
            ClearSelection();
            return handled != null;
        }

        public static void ClearSelection()
        {
            var es = EventSystem.current;
            if (es == null) return;
            var sel = es.currentSelectedGameObject;
            if (sel == null) return;
            if (sel.GetComponent<TMP_InputField>() != null && sel.GetComponent<TMP_InputField>().isFocused) return;
            es.SetSelectedGameObject(null);
        }

        public static void Select(GameObject go)
        {
            var es = EventSystem.current;
            if (es != null) es.SetSelectedGameObject(go);
        }

        /// <summary>Scrolls the nearest ScrollRect so the item is inside its viewport.</summary>
        public static void ScrollIntoView(RectTransform item, ScrollRect scroll)
        {
            if (item == null || scroll == null || scroll.content == null) return;
            try
            {
                Canvas.ForceUpdateCanvases();
                RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
                RectTransform content = scroll.content;
                Vector3 world = item.TransformPoint(item.rect.center);
                Vector3 inViewport = viewport.InverseTransformPoint(world);
                Rect vr = viewport.rect;
                bool outside = inViewport.y > vr.yMax - item.rect.height * 0.5f || inViewport.y < vr.yMin + item.rect.height * 0.5f;
                if (!outside || !scroll.vertical) return;
                float contentHeight = content.rect.height;
                float viewportHeight = vr.height;
                if (contentHeight <= viewportHeight) return;
                Vector3 inContent = content.InverseTransformPoint(world);
                float fromTop = content.rect.yMax - inContent.y;
                float target = fromTop - viewportHeight / 2f;
                float normalized = 1f - Mathf.Clamp01(target / (contentHeight - viewportHeight));
                scroll.verticalNormalizedPosition = normalized;
                Canvas.ForceUpdateCanvases();
            }
            catch
            {
                // Layout quirks must never break navigation.
            }
        }

        /// <summary>Clicks on the next frame (some controls ignore same-frame down+up).</summary>
        public static IEnumerator ClickNextFrame(GameObject target)
        {
            if (target == null) yield break;
            var ped = MakePointer(target);
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerDownHandler);
            yield return null;
            if (target == null) yield break;
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(target, ped, ExecuteEvents.pointerClickHandler);
            ClearSelection();
        }
    }
}

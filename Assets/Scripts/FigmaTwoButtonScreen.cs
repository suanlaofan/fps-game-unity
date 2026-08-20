using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Displays the exact pair of Figma-exported page images for one two-button
/// page. Each invisible Unity button owns a full-page image supplied by Figma;
/// no visual button styling is authored in Unity.
/// </summary>
[DisallowMultipleComponent]
public sealed class FigmaTwoButtonScreen : MonoBehaviour
{
    [Header("Exact Figma page exports")]
    public Image pageImage;
    public Sprite firstButtonPage;
    public Sprite secondButtonPage;

    [Header("Interaction regions")]
    public Button firstButton;
    public Button secondButton;

    private bool hasPointer;

    private void Awake()
    {
        ConfigureButton(firstButton, true);
        ConfigureButton(secondButton, false);
        ShowFirstPage();
    }

    private void OnEnable()
    {
        ShowFirstPage();
    }

    private void Update()
    {
        if (!hasPointer || EventSystem.current == null)
        {
            return;
        }

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected == secondButton?.gameObject)
        {
            ShowSecondPage();
        }
        else if (selected == firstButton?.gameObject)
        {
            ShowFirstPage();
        }
    }

    private void ConfigureButton(Button button, bool first)
    {
        if (button == null)
        {
            return;
        }

        button.transition = Selectable.Transition.None;
        Image hitTarget = button.targetGraphic as Image;
        if (hitTarget != null)
        {
            hitTarget.color = new Color(1f, 1f, 1f, 0f);
            hitTarget.raycastTarget = true;
        }

        FigmaScreenPointerEvents events = button.GetComponent<FigmaScreenPointerEvents>();
        if (events == null)
        {
            events = button.gameObject.AddComponent<FigmaScreenPointerEvents>();
        }
        events.owner = this;
        events.showFirstPage = first;
    }

    public void SetPointerInside(bool inside)
    {
        hasPointer = inside;
    }

    public void ShowFirstPage()
    {
        SetPage(firstButtonPage);
    }

    public void ShowSecondPage()
    {
        SetPage(secondButtonPage);
    }

    private void SetPage(Sprite page)
    {
        if (pageImage != null && page != null && pageImage.sprite != page)
        {
            pageImage.sprite = page;
        }
    }
}

/// <summary>Forwards transparent-button hover and selection feedback.</summary>
[DisallowMultipleComponent]
public sealed class FigmaScreenPointerEvents : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler
{
    [HideInInspector] public FigmaTwoButtonScreen owner;
    [HideInInspector] public bool showFirstPage;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner == null)
        {
            return;
        }

        owner.SetPointerInside(true);
        if (showFirstPage)
        {
            owner.ShowFirstPage();
        }
        else
        {
            owner.ShowSecondPage();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.SetPointerInside(false);
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (showFirstPage)
        {
            owner?.ShowFirstPage();
        }
        else
        {
            owner?.ShowSecondPage();
        }
    }
}

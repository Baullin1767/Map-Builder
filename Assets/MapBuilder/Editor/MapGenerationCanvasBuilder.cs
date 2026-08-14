using MapBuilder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MapBuilderEditor
{
    public static class MapGenerationCanvasBuilder
    {
        private const string CanvasName = "Map Generation Canvas";

        [MenuItem("Tools/Map Builder/Rebuild Map UI")]
        public static void RebuildActiveSceneUI()
        {
            MapGenerationController controller =
                Object.FindAnyObjectByType<MapGenerationController>();
            if (controller == null)
            {
                Debug.LogError("MapGenerationController was not found in the active scene.");
                return;
            }

            Rebuild(controller);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = GameObject.Find(CanvasName);
        }

        public static void Rebuild(MapGenerationController controller)
        {
            GameObject existing = GameObject.Find(CanvasName);
            if (existing != null)
                Object.DestroyImmediate(existing);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject canvasRoot = new GameObject(
                CanvasName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panel = CreateUIObject("Panel", canvasRoot.transform);
            SetTopLeft(panel.GetComponent<RectTransform>(), 20f, 20f, 400f, 430f);
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.035f, 0.055f, 0.05f, 0.96f);

            CreateText(panel.transform, font, "Title", "Генератор карты", 22, FontStyle.Bold,
                new Color(0.75f, 1f, 0.82f), 18f, 16f, 364f, 34f);
            CreateText(panel.transform, font, "Hash Label", "Hash карты", 14, FontStyle.Normal,
                new Color(0.88f, 0.93f, 0.89f), 18f, 58f, 364f, 24f);

            InputField hashInput = CreateInputField(
                panel.transform, font, "Hash Input", "Введите hash", false,
                18f, 84f, 364f, 34f);
            Button newHashButton = CreateButton(
                panel.transform, font, "New Hash Button", "Создать новый hash",
                18f, 126f, 364f, 36f);

            CreateText(panel.transform, font, "Width Label", "Ширина карты (тайлы)", 14,
                FontStyle.Normal, new Color(0.88f, 0.93f, 0.89f),
                18f, 176f, 174f, 24f);
            CreateText(panel.transform, font, "Height Label", "Высота карты (тайлы)", 14,
                FontStyle.Normal, new Color(0.88f, 0.93f, 0.89f),
                208f, 176f, 174f, 24f);

            InputField widthInput = CreateInputField(
                panel.transform, font, "Width Input", "64", true,
                18f, 202f, 174f, 34f);
            InputField heightInput = CreateInputField(
                panel.transform, font, "Height Input", "64", true,
                208f, 202f, 174f, 34f);
            Button generateButton = CreateButton(
                panel.transform, font, "Generate Button", "Сгенерировать карту по hash",
                18f, 252f, 364f, 40f);

            Text statusLabel = CreateText(
                panel.transform, font, "Status", "Готово к генерации", 13, FontStyle.Normal,
                new Color(0.68f, 0.82f, 0.72f), 18f, 308f, 364f, 70f);
            statusLabel.verticalOverflow = VerticalWrapMode.Overflow;

            MapGenerationUI ui = controller.GetComponent<MapGenerationUI>();
            if (ui == null)
                ui = controller.gameObject.AddComponent<MapGenerationUI>();
            ui.Configure(
                controller, hashInput, widthInput, heightInput,
                newHashButton, generateButton, statusLabel);
            EditorUtility.SetDirty(ui);

            EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem != null)
            {
                StandaloneInputModule oldModule =
                    eventSystem.GetComponent<StandaloneInputModule>();
                if (oldModule != null)
                    Object.DestroyImmediate(oldModule);

                InputSystemUIInputModule existingModule =
                    eventSystem.GetComponent<InputSystemUIInputModule>();
                if (existingModule == null)
                {
                    existingModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                    existingModule.AssignDefaultActions();
                }
                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            InputSystemUIInputModule module =
                eventSystemObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private static InputField CreateInputField(
            Transform parent, Font font, string name, string placeholderValue, bool integerOnly,
            float x, float y, float width, float height)
        {
            GameObject fieldObject = CreateUIObject(name, parent);
            SetTopLeft(fieldObject.GetComponent<RectTransform>(), x, y, width, height);
            Image background = fieldObject.AddComponent<Image>();
            background.color = new Color(0.035f, 0.045f, 0.042f, 1f);

            InputField field = fieldObject.AddComponent<InputField>();
            field.targetGraphic = background;
            field.lineType = InputField.LineType.SingleLine;
            field.characterValidation = integerOnly
                ? InputField.CharacterValidation.Integer
                : InputField.CharacterValidation.None;

            Text text = CreateStretchText(
                fieldObject.transform, font, "Text", string.Empty, 14,
                new Color(0.94f, 0.96f, 0.94f), 8f, 8f);
            text.supportRichText = false;
            text.alignment = TextAnchor.MiddleLeft;

            Text placeholder = CreateStretchText(
                fieldObject.transform, font, "Placeholder", placeholderValue, 14,
                new Color(0.55f, 0.6f, 0.57f), 8f, 8f);
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.alignment = TextAnchor.MiddleLeft;

            field.textComponent = text;
            field.placeholder = placeholder;
            return field;
        }

        private static Button CreateButton(
            Transform parent, Font font, string name, string caption,
            float x, float y, float width, float height)
        {
            GameObject buttonObject = CreateUIObject(name, parent);
            SetTopLeft(buttonObject.GetComponent<RectTransform>(), x, y, width, height);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.16f, 0.22f, 0.18f, 1f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.16f, 0.22f, 0.18f, 1f);
            colors.highlightedColor = new Color(0.22f, 0.34f, 0.26f, 1f);
            colors.pressedColor = new Color(0.1f, 0.16f, 0.12f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            Text captionText = CreateStretchText(
                buttonObject.transform, font, "Label", caption, 14,
                new Color(0.9f, 0.96f, 0.91f), 6f, 6f);
            captionText.fontStyle = FontStyle.Bold;
            captionText.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private static Text CreateText(
            Transform parent, Font font, string name, string value, int fontSize,
            FontStyle fontStyle, Color color,
            float x, float y, float width, float height)
        {
            GameObject textObject = CreateUIObject(name, parent);
            SetTopLeft(textObject.GetComponent<RectTransform>(), x, y, width, height);
            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private static Text CreateStretchText(
            Transform parent, Font font, string name, string value, int fontSize,
            Color color, float horizontalPadding, float verticalPadding)
        {
            GameObject textObject = CreateUIObject(name, parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
            rect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);

            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            return text;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void SetTopLeft(
            RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}

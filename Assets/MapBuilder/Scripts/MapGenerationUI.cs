using System;
using UnityEngine;
using UnityEngine.UI;

namespace MapBuilder
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MapGenerationController))]
    public sealed class MapGenerationUI : MonoBehaviour
    {
        private const int MinMapSize = 16;
        private const int MaxMapSize = 256;

        [SerializeField] private MapGenerationController controller;
        [SerializeField] private InputField hashInput;
        [SerializeField] private InputField widthInput;
        [SerializeField] private InputField heightInput;
        [SerializeField] private Button newHashButton;
        [SerializeField] private Button generateButton;
        [SerializeField] private Text statusLabel;

        public void Configure(
            MapGenerationController generationController,
            InputField hashField,
            InputField widthField,
            InputField heightField,
            Button hashButton,
            Button mapButton,
            Text statusText)
        {
            controller = generationController;
            hashInput = hashField;
            widthInput = widthField;
            heightInput = heightField;
            newHashButton = hashButton;
            generateButton = mapButton;
            statusLabel = statusText;
        }

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<MapGenerationController>();

            if (!HasCompleteUI())
            {
                Debug.LogError(
                    "MapGenerationUI is not configured. Rebuild it from Tools/Map Builder/Rebuild Map UI.",
                    this);
                enabled = false;
                return;
            }

            hashInput.text = string.IsNullOrEmpty(controller.CurrentHash)
                ? CreateHash()
                : controller.CurrentHash;
            widthInput.text = controller.Settings.width.ToString();
            heightInput.text = controller.Settings.height.ToString();
            statusLabel.text = "Готово к генерации";

            newHashButton.onClick.AddListener(GenerateHash);
            generateButton.onClick.AddListener(GenerateMap);
        }

        private void OnDestroy()
        {
            if (newHashButton != null)
                newHashButton.onClick.RemoveListener(GenerateHash);
            if (generateButton != null)
                generateButton.onClick.RemoveListener(GenerateMap);
        }

        private bool HasCompleteUI()
        {
            return controller != null && hashInput != null && widthInput != null &&
                heightInput != null && newHashButton != null && generateButton != null &&
                statusLabel != null;
        }

        private void GenerateHash()
        {
            hashInput.text = CreateHash();
            statusLabel.text = "Новый hash создан";
        }

        private void GenerateMap()
        {
            int width;
            int height;
            if (!TryReadSize(widthInput, "Ширина", out width) ||
                !TryReadSize(heightInput, "Высота", out height))
            {
                return;
            }

            string normalizedHash = (hashInput.text ?? string.Empty).Trim();
            if (normalizedHash.Length == 0)
            {
                statusLabel.text = "Введите hash или создайте новый";
                return;
            }

            controller.SetMapSize(width, height);
            if (controller.GenerateFromHash(normalizedHash))
            {
                hashInput.text = normalizedHash;
                statusLabel.text = string.Format(
                    "Карта {0}x{1} сгенерирована",
                    controller.LastLayout.Width,
                    controller.LastLayout.Height);
            }
            else
            {
                statusLabel.text = "Не удалось сгенерировать карту";
            }
        }

        private bool TryReadSize(InputField input, string label, out int value)
        {
            if (!int.TryParse(input.text, out value))
            {
                statusLabel.text = label + " должна быть целым числом";
                return false;
            }

            value = Mathf.Clamp(value, MinMapSize, MaxMapSize);
            input.text = value.ToString();
            return true;
        }

        private static string CreateHash()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}

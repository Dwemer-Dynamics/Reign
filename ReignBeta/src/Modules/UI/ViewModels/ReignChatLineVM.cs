using System;
using System.Text;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignChatLineVM : ViewModel
    {
        private const int MaxUnbrokenRunChars = 64;
        private string _speaker;
        private string _text;
        private string _role;
        private string _rawText;
        private string _sceneImageId = "";
        private Action _openScene;
        public string ExchangeId { get; set; } = "";
        internal string OriginalText => _rawText ?? "";
        internal string InlineScenePath { get; set; } = "";
        internal string FullScenePath { get; set; } = "";
        [DataSourceProperty] public bool IsViewLine => _role == "view";
        [DataSourceProperty] public bool IsTextLine => !IsViewLine;
        [DataSourceProperty] public bool HasSceneImage => !string.IsNullOrEmpty(_sceneImageId);
        [DataSourceProperty] public string SceneImageId => _sceneImageId;
        [DataSourceProperty] public float SceneCaptionTop => HasSceneImage ? 220f : 12f;
        [DataSourceProperty] public float SceneCompactCaptionTop => HasSceneImage ? 112f : 12f;
        [DataSourceProperty] public string SceneCaption => _text != null && _text.Length > 96 ? _text.Substring(0, 93) + "..." : _text;
        public void ExecuteOpenScene() => _openScene?.Invoke();
        internal void SetScene(string imageId, string caption, Action open)
        {
            _sceneImageId = imageId ?? ""; _openScene = open; Text = caption;
            OnPropertyChanged(nameof(SceneImageId)); OnPropertyChanged(nameof(HasSceneImage));
            OnPropertyChanged(nameof(SceneCaptionTop));
            OnPropertyChanged(nameof(SceneCompactCaptionTop));
        }

        public ReignChatLineVM(string speaker, string text, string role)
        {
            _speaker = SanitizeDisplayText(speaker);
            _rawText = text ?? string.Empty;
            _text = SanitizeDisplayText(text);
            Role = role;
        }

        [DataSourceProperty]
        public string Speaker
        {
            get { return _speaker; }
            set
            {
                string clean = SanitizeDisplayText(value);
                if (clean != _speaker)
                {
                    _speaker = clean;
                    OnPropertyChangedWithValue(clean);
                }
            }
        }

        [DataSourceProperty]
        public string Role
        {
            get { return _role; }
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "system" : value.ToLowerInvariant();
                if (normalized != _role)
                {
                    _role = normalized;
                    OnPropertyChangedWithValue(normalized);
                    OnPropertyChanged(nameof(IsPlayerLine));
                    OnPropertyChanged(nameof(IsNpcLine));
                    OnPropertyChanged(nameof(IsNonPlayerLine));
                    OnPropertyChanged(nameof(IsSystemLine));
                    OnPropertyChanged(nameof(IsViewLine));
                    OnPropertyChanged(nameof(IsTextLine));
                    OnPropertyChanged(nameof(RichText));
                }
            }
        }

        [DataSourceProperty]
        public bool IsPlayerLine
        {
            get { return _role == "player"; }
        }

        [DataSourceProperty]
        public bool IsNpcLine
        {
            get { return _role == "npc"; }
        }

        [DataSourceProperty]
        public bool IsSystemLine
        {
            get { return !IsPlayerLine && !IsNpcLine && !IsViewLine; }
        }

        [DataSourceProperty]
        public bool IsNonPlayerLine => !IsPlayerLine && !IsViewLine;

        [DataSourceProperty]
        public string Text
        {
            get { return _text; }
            set
            {
                _rawText = value ?? string.Empty;
                OnPropertyChanged(nameof(RichText));
                string clean = SanitizeDisplayText(value);
                if (clean != _text)
                {
                    _text = clean;
                    OnPropertyChangedWithValue(clean);
                    OnPropertyChanged(nameof(SceneCaption));
                }
            }
        }

        [DataSourceProperty]
        public string RichText => ReignBeta.Dialogue.ReignActionText.ToRichText(_rawText, IsNpcLine);

        internal static string SanitizeDisplayText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            int unbrokenRun = 0;
            int newlineRun = 0;
            bool lastWasSpace = false;

            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '\r')
                {
                    continue;
                }

                if (ch == '\n')
                {
                    if (builder.Length > 0 && newlineRun < 2)
                    {
                        TrimTrailingSpace(builder);
                        builder.Append('\n');
                        newlineRun++;
                    }

                    lastWasSpace = false;
                    unbrokenRun = 0;
                    continue;
                }

                char safe = SafeDisplayChar(ch);
                if (safe == '\0')
                {
                    continue;
                }

                if (safe == ' ')
                {
                    if (builder.Length == 0 || lastWasSpace || newlineRun > 0)
                    {
                        continue;
                    }

                    builder.Append(' ');
                    lastWasSpace = true;
                    unbrokenRun = 0;
                    newlineRun = 0;
                    continue;
                }

                if (unbrokenRun >= MaxUnbrokenRunChars)
                {
                    builder.Append(' ');
                    unbrokenRun = 0;
                }

                builder.Append(safe);
                lastWasSpace = false;
                newlineRun = 0;
                unbrokenRun++;
            }

            TrimTrailingSpace(builder);
            while (builder.Length > 0 && builder[builder.Length - 1] == '\n')
            {
                builder.Length--;
            }

            return builder.ToString();
        }

        private static char SafeDisplayChar(char ch)
        {
            switch (ch)
            {
                case '*':
                case '`':
                    return '\0';
                case '[':
                case '{':
                case '<':
                    return '(';
                case ']':
                case '}':
                case '>':
                    return ')';
                case '\t':
                case '\u00A0':
                    return ' ';
                case '\u2018':
                case '\u2019':
                    return '\'';
                case '\u201C':
                case '\u201D':
                    return '"';
                case '\u2013':
                case '\u2014':
                case '\u2212':
                    return '-';
                case '\u2022':
                    return '-';
                case '\u2026':
                    return '.';
                case '\u00AD':
                    return '\0';
                default:
                    if (char.IsControl(ch) || char.IsSurrogate(ch))
                    {
                        return '\0';
                    }

                    if (char.IsWhiteSpace(ch))
                    {
                        return ' ';
                    }

                    return ch >= 32 && ch <= 126 ? ch : '?';
            }
        }

        private static void TrimTrailingSpace(StringBuilder builder)
        {
            while (builder.Length > 0 && builder[builder.Length - 1] == ' ')
            {
                builder.Length--;
            }
        }
    }
}

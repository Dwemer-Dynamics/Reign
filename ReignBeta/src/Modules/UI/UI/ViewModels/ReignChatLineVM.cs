using System.Text;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignChatLineVM : ViewModel
    {
        private const int MaxDisplayTextChars = 4000;
        private const int MaxUnbrokenRunChars = 64;
        private string _speaker;
        private string _text;
        private string _role;
        private string _rawText;

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
                    OnPropertyChanged(nameof(IsSystemLine));
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
            get { return !IsPlayerLine && !IsNpcLine; }
        }

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

            StringBuilder builder = new StringBuilder(value.Length > MaxDisplayTextChars ? MaxDisplayTextChars + 3 : value.Length);
            int unbrokenRun = 0;
            int newlineRun = 0;
            bool lastWasSpace = false;
            bool truncated = false;

            for (int i = 0; i < value.Length; i++)
            {
                if (builder.Length >= MaxDisplayTextChars)
                {
                    truncated = true;
                    break;
                }

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

            if (truncated)
            {
                builder.Append("...");
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

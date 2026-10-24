extern alias NumericsVectors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ReignBeta.UI.ViewModels;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;
using TaleWorlds.TwoDimension;
using Vector2 = NumericsVectors::System.Numerics.Vector2;

namespace ReignBeta.UI.Widgets
{
    public sealed class ReignActionEditableTextWidget : EditableTextWidget
    {
        // Read the native horizontal viewport, never infer it by searching equal text.
        // The compatibility contract verifies these two read-only fields against the
        // installed native assembly. Cursor, selection and hit testing remain native.
        private static readonly FieldInfo VisibleStart = typeof(EditableText).GetField("_visibleStart", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo VisibleText = typeof(EditableText).GetField("_realVisibleText", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly RichText _styledText;
        private int _seenVersion = -1, _deleteRepeatAt;
        private InputKey _heldDelete = InputKey.Invalid;
        private bool _styledReady;
        public static bool IsNativeTextCompatible => VisibleStart?.FieldType == typeof(int) && VisibleText?.FieldType == typeof(string);
        public string ComposerId { get; set; }
        private bool _missingComposerReported;

        public void BindComposer(ReignActionComposerVM composer)
        {
            if (composer == null || ReignActionComposerVM.Find(composer.Id) != composer)
                throw new ArgumentException("A registered action composer is required.", nameof(composer));
            ComposerId = composer.Id;
            _missingComposerReported = false;
        }

        public ReignActionEditableTextWidget(UIContext context) : base(context)
        {
            _styledText = new RichText(0, 0, context.FontFactory.DefaultFont, context.FontFactory.GetUsableFontForCharacter);
        }

        public override void HandleInput(IReadOnlyList<int> keys)
        {
            if (IsDisabled) return;
            var composer = ReignActionComposerVM.Find(ComposerId);
            if (composer == null)
            {
                if (!_missingComposerReported)
                {
                    ReignBeta.Integration.ReignLog.Warn("Action editor has no registered composer: widget=" + Id);
                    _missingComposerReported = true;
                }
                return;
            }
            CaptureSelection(composer);
            bool control = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool alt = Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt);
            if (control && !alt && Input.IsKeyPressed(InputKey.A)) { _editableText.SelectAll(); CaptureSelection(composer); return; }
            if (control && !alt && Input.IsKeyPressed(InputKey.C)) { CopyText(_editableText.SelectedTextBegin, _editableText.SelectedTextEnd); return; }
            if (control && !alt && Input.IsKeyPressed(InputKey.X))
            {
                CopyText(_editableText.SelectedTextBegin, _editableText.SelectedTextEnd);
                ReplaceSelection(composer, ""); return;
            }
            if (control && !alt && Input.IsKeyPressed(InputKey.V))
            {
                string pasted = new string((Input.GetClipboardText() ?? "").Where(c => !char.IsControl(c) && c != '<' && c != '>').ToArray());
                ReplaceSelection(composer, pasted); return;
            }
            InputKey deleting = Input.IsKeyDown(InputKey.BackSpace) ? InputKey.BackSpace : Input.IsKeyDown(InputKey.Delete) ? InputKey.Delete : InputKey.Invalid;
            if (deleting != InputKey.Invalid)
            {
                int now = Environment.TickCount;
                bool first = deleting != _heldDelete || Input.IsKeyPressed(deleting);
                _heldDelete = deleting;
                if (first || unchecked(now - _deleteRepeatAt) >= 0)
                {
                    _deleteRepeatAt = unchecked(now + (first ? 500 : 30));
                    int start = _editableText.SelectedTextBegin, end = _editableText.SelectedTextEnd;
                    if (start == end)
                    {
                        int caret = _editableText.CursorPosition;
                        start = deleting == InputKey.BackSpace ? (control ? _editableText.FindNextWordPosition(-1) : PreviousCharacter(Text, caret)) : caret;
                        end = deleting == InputKey.Delete ? (control ? _editableText.FindNextWordPosition(1) : NextCharacter(Text, caret)) : caret;
                    }
                    if (end > start) { composer.Replace(start, end - start, ""); Sync(composer); }
                }
                return;
            }
            _heldDelete = InputKey.Invalid;
            if (!alt && !control || Input.IsKeyDown(InputKey.RightAlt))
            {
                var typed = new StringBuilder();
                foreach (int code in keys)
                    if (code >= 32 && (code < 127 || code >= 160) && code <= char.MaxValue && code != '<' && code != '>') typed.Append((char)code);
                if (typed.Length > 0) { ReplaceSelection(composer, typed.ToString()); return; }
            }
            // Native cursor movement, Shift-selection, Home/End and Enter semantics.
            base.HandleInput(Array.Empty<int>());
            CaptureSelection(composer);
        }

        private static int PreviousCharacter(string text, int caret) => caret <= 0 ? 0 : caret > 1 && char.IsLowSurrogate(text[caret - 1]) && char.IsHighSurrogate(text[caret - 2]) ? caret - 2 : caret - 1;
        private static int NextCharacter(string text, int caret) => caret >= text.Length ? text.Length : caret + 1 < text.Length && char.IsHighSurrogate(text[caret]) && char.IsLowSurrogate(text[caret + 1]) ? caret + 2 : caret + 1;
        private void ReplaceSelection(ReignActionComposerVM composer, string value)
        {
            int start = _editableText.SelectedTextBegin, end = _editableText.SelectedTextEnd;
            if (start == end) start = end = _editableText.CursorPosition;
            if (start == end && value.Length == 0) return;
            int room = MaxLength < 0 ? int.MaxValue : Math.Max(0, MaxLength - (Text.Length - (end - start)));
            if (value.Length > room) value = value.Substring(0, room);
            composer.Replace(start, end - start, value); Sync(composer);
        }
        private void CaptureSelection(ReignActionComposerVM composer)
        {
            int caret = _editableText.CursorPosition;
            composer.State.Select(caret, _editableText.IsAnySelected() ? _editableText.SelectedTextBegin : caret,
                _editableText.IsAnySelected() ? _editableText.SelectedTextEnd : caret);
        }
        private void Sync(ReignActionComposerVM composer)
        {
            Text = composer.State.Text;
            _editableText.SetCursor(composer.State.Caret, IsFocused);
            _editableText.ResetSelected();
            _seenVersion = composer.State.Version;
        }
        protected override void OnLoseFocus()
        {
            var composer = ReignActionComposerVM.Find(ComposerId);
            if (composer != null) CaptureSelection(composer);
            base.OnLoseFocus();
        }
        protected override void OnUpdate(float dt)
        {
            var composer = ReignActionComposerVM.Find(ComposerId);
            if (composer != null && composer.State.Version != _seenVersion) Sync(composer);
            base.OnUpdate(dt);
        }
        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            _styledReady = false;
            var composer = ReignActionComposerVM.Find(ComposerId);
            if (composer == null || !composer.State.HasValidAttachment || !IsNativeTextCompatible) return;
            int start = (int)VisibleStart.GetValue(_editableText);
            string visible = (string)VisibleText.GetValue(_editableText) ?? "";
            if (start < 0 || start > Text.Length - visible.Length || !string.Equals(Text.Substring(start, visible.Length), visible, StringComparison.Ordinal)) return;
            _styledText.CurrentLanguage = Context.FontFactory.CurrentLanguage;
            _styledText.StyleFontContainer.ClearFonts();
            foreach (string name in new[] { "Default", "Highlight", "Action" })
            {
                var style = ReadOnlyBrush.GetStyleOrDefault(name);
                var font = style.Font ?? ReadOnlyBrush.Font ?? Context.FontFactory.DefaultFont;
                _styledText.StyleFontContainer.Add(name, Context.FontFactory.GetMappedFontForLocalization(font.Name), style.FontSize * _scaleToUse);
            }
            _styledText.HorizontalAlignment = _editableText.HorizontalAlignment;
            _styledText.VerticalAlignment = _editableText.VerticalAlignment;
            _styledText.UpdateSize((int)Size.X, (int)Size.Y);
            var formatted = new StringBuilder();
            string previous = null;
            for (int i = 0; i < visible.Length; i++)
            {
                int position = start + i;
                string style = position >= _editableText.SelectedTextBegin && position < _editableText.SelectedTextEnd ? "Highlight"
                    : position >= composer.State.ActionStart && position < composer.State.ActionStart + composer.State.ActionLength ? "Action" : "Default";
                if (style != previous)
                {
                    if (previous != null) formatted.Append("</span>");
                    formatted.Append("<span style=\"").Append(style).Append("\">"); previous = style;
                }
                // Native input excludes markup. Keep programmatic text from becoming styles.
                formatted.Append(visible[i] == '<' || visible[i] == '>' ? ' ' : visible[i]);
            }
            if (previous != null) formatted.Append("</span>");
            _styledText.Value = formatted.ToString();
            // RichText does not position tokens until its preferred size has been
            // measured. Update alone leaves every glyph at the origin.
            _styledText.GetPreferredSize(true, Size.X, true, Size.Y, Context.SpriteData, _scaleToUse);
            _styledText.Update(dt, Context.SpriteData, Vector2.Zero, false, true, true, _scaleToUse);
            _styledReady = true;
            if (IsFocused) CaptureSelection(composer);
        }
        protected override void OnRender(TwoDimensionContext context, TwoDimensionDrawContext drawContext)
        {
            if (!_styledReady) { base.OnRender(context, drawContext); return; }
            // Reuse native rendering, selection highlight and caret drawing; only the
            // already-laid-out glyph runs change color. Never alter native editor text.
            var parts = _editableText.GetParts();
            var original = parts.ToArray();
            try { parts.Clear(); parts.AddRange(_styledText.GetParts()); base.OnRender(context, drawContext); }
            finally { parts.Clear(); parts.AddRange(original); }
        }
    }
}

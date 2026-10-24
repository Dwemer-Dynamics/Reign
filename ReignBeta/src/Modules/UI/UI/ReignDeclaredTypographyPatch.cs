using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI
{
    /// <summary>
    /// Bannerlord 1.4.8's interpreted prefab loader does not convert Font values,
    /// and Brush exposes none of the TextOutline/Glow/Blur properties. Bind only
    /// Reign's explicitly requested faces to a widget-owned brush and apply the
    /// approved effect-free styles there; never alter the shared native brush.
    /// </summary>
    internal static class ReignDeclaredTypographyPatch
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.declared_typography";
        private static readonly HashSet<string> ReportedFailures = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> ReportedFonts = new HashSet<string>(StringComparer.Ordinal);
        private static Harmony _harmony;
        private static MethodInfo _setFontSprite;

        public static void Apply()
        {
            if (_harmony != null) return;
            try
            {
                Type extensions = AccessTools.TypeByName("TaleWorlds.GauntletUI.PrefabSystem.WidgetExtensions");
                MethodInfo setter = extensions == null ? null : AccessTools.Method(extensions, "SetWidgetAttributeFromString");
                if (setter == null) throw new MissingMethodException("Gauntlet's interpreted attribute loader was not found.");
                _setFontSprite = AccessTools.PropertySetter(typeof(Font), nameof(Font.FontSprite));
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(setter, prefix: new HarmonyMethod(typeof(ReignDeclaredTypographyPatch), nameof(BeforeSetAttribute)));
                ReignLog.Info("Reign declared-font compatibility loaded.");
            }
            catch (Exception ex)
            {
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
                ReignLog.Warn("Reign declared-font compatibility could not load: " + ex);
            }
        }

        public static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            _setFontSprite = null;
            ReportedFailures.Clear();
            ReportedFonts.Clear();
        }

        private static bool BeforeSetAttribute(object target, string name, string value)
        {
            if (name != "Brush.Font" || !(target is BrushWidget widget)
                || (value != "ReignSerifDynamic" && value != "ReignSerifActionItalic")) return true;

            try
            {
                string category = value == "ReignSerifDynamic" ? "ui_reignbeta_fonts" : "ui_reignbeta_action_fonts";
                if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded(category))
                    throw new InvalidOperationException("The declared font's registered sprite category is unavailable.");

                Font font = widget.Context.FontFactory.GetFont(value);
                if (font == null || !string.Equals(font.Name, value, StringComparison.Ordinal))
                    throw new InvalidOperationException("The declared font resolved to a fallback face.");

                // Fonts can be discovered before a module's sprite registration.
                // Repair that exact missing page without clearing/reloading glyphs
                // or changing the language/default-font factory.
                if (font.FontSprite == null)
                {
                    SpritePart page = (widget.Context.SpriteData.GetSprite(value) as SpriteGeneric)?.SpritePart;
                    if (page == null || _setFontSprite == null)
                        throw new InvalidOperationException("The declared font's registered page is unavailable.");
                    _setFontSprite.Invoke(font, new object[] { page });
                }
                if (font.FontSprite.Texture == null || !font.FontSprite.Texture.IsValid)
                    throw new InvalidOperationException("The declared font page has no loaded texture.");

                // Brush's getter creates the per-widget clone. Named rich-text
                // style fonts (including action italics) retain their own faces.
                var brush = widget.Brush;
                brush.Font = font;
                foreach (var style in brush.Styles)
                {
                    style.TextOutlineAmount = 0f;
                    style.TextGlowRadius = 0f;
                    style.TextBlur = 0f;
                    style.TextShadowOffset = 0f;
                }
                if (ReportedFonts.Add(value))
                    ReignLog.Info("Reign declared font bound: " + value + "; page=" + font.FontSprite.Name + "; synthetic effects disabled.");
            }
            catch (Exception ex)
            {
                if (ReportedFailures.Add(value))
                    ReignLog.Warn("Reign declared font could not bind " + value + ": " + ex.Message);
            }
            return false;
        }
    }
}

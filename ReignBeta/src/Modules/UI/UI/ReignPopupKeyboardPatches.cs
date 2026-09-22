using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace ReignBeta.UI
{
    /// <summary>
    /// Gives Reign-owned native inquiries predictable physical keyboard behavior.
    /// Bannerlord's query manager listens to configurable Confirm/Exit hotkeys;
    /// this bridge supplements those bindings with Enter/Numpad Enter and Escape
    /// without changing inquiries opened by the base game or other modules.
    /// </summary>
    internal static class ReignPopupKeyboardPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.popup_keyboard";
        private static readonly object Sync = new object();
        private static readonly HashSet<object> ReignInquiries = new HashSet<object>();
        private static readonly Assembly ReignAssembly = typeof(ReignPopupKeyboardPatches).Assembly;
        private static Harmony _harmony;
        private static FieldInfo _activeDataSourceField;
        private static FieldInfo _activeQueryDataField;

        public static void Apply()
        {
            if (_harmony != null) return;

            try
            {
                Type queryManager = AccessTools.TypeByName(
                    "TaleWorlds.MountAndBlade.GauntletUI.GauntletQueryManager");
                MethodInfo earlyTick = queryManager == null
                    ? null
                    : AccessTools.Method(queryManager, "OnEarlyTick");
                MethodInfo closeQuery = queryManager == null
                    ? null
                    : AccessTools.Method(queryManager, "CloseQuery");
                _activeDataSourceField = queryManager == null
                    ? null
                    : AccessTools.Field(queryManager, "_activeDataSource");
                _activeQueryDataField = queryManager == null
                    ? null
                    : AccessTools.Field(queryManager, "_activeQueryData");
                if (earlyTick == null || closeQuery == null
                    || _activeDataSourceField == null || _activeQueryDataField == null)
                {
                    throw new MissingMemberException(
                        "Bannerlord's Gauntlet query-manager keyboard surface could not be resolved.");
                }

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    AccessTools.Method(typeof(InformationManager), nameof(InformationManager.ShowInquiry),
                        new[] { typeof(InquiryData), typeof(bool), typeof(bool) }),
                    prefix: new HarmonyMethod(typeof(ReignPopupKeyboardPatches), nameof(RegisterInquiryPrefix)));
                _harmony.Patch(
                    AccessTools.Method(typeof(InformationManager), nameof(InformationManager.ShowTextInquiry),
                        new[] { typeof(TextInquiryData), typeof(bool), typeof(bool) }),
                    prefix: new HarmonyMethod(typeof(ReignPopupKeyboardPatches), nameof(RegisterTextInquiryPrefix)));
                _harmony.Patch(
                    AccessTools.Method(typeof(MBInformationManager), nameof(MBInformationManager.ShowMultiSelectionInquiry),
                        new[] { typeof(MultiSelectionInquiryData), typeof(bool), typeof(bool) }),
                    prefix: new HarmonyMethod(typeof(ReignPopupKeyboardPatches), nameof(RegisterMultiSelectionInquiryPrefix)));
                _harmony.Patch(
                    earlyTick,
                    prefix: new HarmonyMethod(typeof(ReignPopupKeyboardPatches), nameof(QueryManagerEarlyTickPrefix)));
                _harmony.Patch(
                    closeQuery,
                    prefix: new HarmonyMethod(typeof(ReignPopupKeyboardPatches), nameof(QueryManagerClosePrefix)));
                ReignLog.Info("Reign popup Enter/Escape keyboard routing loaded.");
            }
            catch (Exception ex)
            {
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
                _activeDataSourceField = null;
                _activeQueryDataField = null;
                lock (Sync) ReignInquiries.Clear();
                ReignLog.Warn("Reign popup keyboard routing failed to load: " + ex);
            }
        }

        public static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            _activeDataSourceField = null;
            _activeQueryDataField = null;
            lock (Sync) ReignInquiries.Clear();
        }

        private static void RegisterInquiryPrefix(InquiryData data)
        {
            RegisterIfOwnedByReign(data);
        }

        private static void RegisterTextInquiryPrefix(TextInquiryData textData)
        {
            RegisterIfOwnedByReign(textData);
        }

        private static void RegisterMultiSelectionInquiryPrefix(MultiSelectionInquiryData data)
        {
            RegisterIfOwnedByReign(data);
        }

        private static void RegisterIfOwnedByReign(object data)
        {
            if (data == null || !IsReignCaller()) return;
            lock (Sync) ReignInquiries.Add(data);
        }

        private static bool IsReignCaller()
        {
            StackFrame[] frames = new StackTrace(false).GetFrames();
            if (frames == null) return false;
            foreach (StackFrame frame in frames)
            {
                Type declaringType = frame.GetMethod()?.DeclaringType;
                if (declaringType != null
                    && declaringType != typeof(ReignPopupKeyboardPatches)
                    && declaringType.Assembly == ReignAssembly)
                {
                    return true;
                }
            }
            return false;
        }

        private static void QueryManagerEarlyTickPrefix()
        {
            object queryData = _activeQueryDataField?.GetValue(null);
            object popup = _activeDataSourceField?.GetValue(null);
            if (queryData == null || popup == null || !IsRegistered(queryData)) return;

            if ((Input.IsKeyReleased(InputKey.Enter)
                    || Input.IsKeyReleased(InputKey.NumpadEnter))
                && ReadBoolProperty(popup, "IsButtonOkShown")
                && ReadBoolProperty(popup, "IsButtonOkEnabled"))
            {
                InvokePopupCommand(popup, "ExecuteAffirmativeAction");
                return;
            }

            if (!Input.IsKeyReleased(InputKey.Escape)) return;
            if (ReadBoolProperty(popup, "IsButtonCancelShown")
                && ReadBoolProperty(popup, "IsButtonCancelEnabled"))
            {
                InvokePopupCommand(popup, "ExecuteNegativeAction");
            }
            else if (ReadBoolProperty(popup, "IsButtonOkShown")
                && ReadBoolProperty(popup, "IsButtonOkEnabled")
                && IsAcknowledgementLabel(ReadStringProperty(popup, "ButtonOkLabel")))
            {
                InvokePopupCommand(popup, "ExecuteAffirmativeAction");
            }
        }

        private static void QueryManagerClosePrefix()
        {
            object queryData = _activeQueryDataField?.GetValue(null);
            if (queryData == null) return;
            lock (Sync) ReignInquiries.Remove(queryData);
        }

        private static bool IsRegistered(object data)
        {
            lock (Sync) return ReignInquiries.Contains(data);
        }

        private static bool ReadBoolProperty(object target, string propertyName)
        {
            object value = AccessTools.Property(target.GetType(), propertyName)?.GetValue(target, null);
            return value is bool flag && flag;
        }

        private static string ReadStringProperty(object target, string propertyName)
        {
            return AccessTools.Property(target.GetType(), propertyName)?.GetValue(target, null) as string
                ?? string.Empty;
        }

        private static void InvokePopupCommand(object target, string methodName)
        {
            AccessTools.Method(target.GetType(), methodName)?.Invoke(target, null);
        }

        private static bool IsAcknowledgementLabel(string label)
        {
            string normalized = (label ?? string.Empty).Trim();
            return normalized.Equals("Acknowledge", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("OK", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Okay", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Close", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Continue", StringComparison.OrdinalIgnoreCase);
        }
    }
}

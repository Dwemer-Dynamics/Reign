using System;
using System.Collections.Generic;
using System.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public interface IReignActionHost
    {
        IReadOnlyList<Hero> ActionParticipants { get; }
        bool ActionBusy { get; }
        ReignActionComposerVM Composer { get; }
    }

    public static class ReignActionBuilder
    {
        private static ScreenBase _screen;
        private static GauntletLayer _layer;
        private static ScreenLayer _previousFocus;
        private static IReignActionHost _owner;
        private static ReignActionBuilderVM _vm;
        public static bool IsOpen => _layer != null;
        public static bool IsCalibrationFixture { get; private set; }

        public static bool Handle(ScreenBase screen, IReignActionHost owner)
        {
            if (IsOpen) return true;
            if (owner == null || owner.ActionBusy || screen == null || screen != ScreenManager.TopScreen) return false;
            if (!(Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) || !Input.IsKeyReleased(InputKey.A)) return false;
            return TryOpenForHost(screen, owner);
        }

        internal static bool TryOpenForHost(ScreenBase screen, IReignActionHost owner)
        {
            if (IsOpen || owner == null || owner.ActionBusy || screen == null || screen != ScreenManager.TopScreen) return false;
            var participants = owner.ActionParticipants.Where(h => h != null && !h.IsDead && !h.IsChild && h != Hero.MainHero).ToList();
            if (participants.Count == 0) return false;
            if (!Widgets.ReignActionEditableTextWidget.IsNativeTextCompatible)
                throw new InvalidOperationException("The native text editor is incompatible with protected Reign action sentences.");
            _owner = owner;
            Open(screen, new ReignActionBuilderVM(participants, a =>
            {
                owner.Composer.Insert(a);
                Close();
            }, Close, owner.Composer.State.Draft));
            return true;
        }

        public static void OpenForCalibration(Hero hero)
        {
            if (hero == null || Hero.MainHero == null) throw new InvalidOperationException("A campaign character is required.");
            Close();
            IsCalibrationFixture = true;
            // Accept closes a temporary draft only; no host, provider or executor exists.
            Open(ScreenManager.TopScreen, new ReignActionBuilderVM(new[] { hero }, _ => Close(), Close));
        }

        private static void Open(ScreenBase screen, ReignActionBuilderVM vm)
        {
            _screen = screen; _vm = vm; _previousFocus = ScreenManager.FocusedLayer;
            try
            {
                _layer = new GauntletLayer("ReignActionBuilder", 10020, true);
                _layer.LoadMovie("ReignActionBuilder", _vm);
                _layer.Input.IsKeysAllowed = true;
                _layer.IsFocusLayer = true; _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                screen.AddLayer(_layer); screen.MouseVisible = true; ScreenManager.TrySetFocus(_layer);
            }
            catch { Close(); throw; }
        }

        public static void ApplicationTick()
        {
            if (!IsOpen) return;
            if (_screen != ScreenManager.TopScreen) { Close(); return; }
            _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            ScreenManager.TrySetFocus(_layer);
            if (Input.IsKeyReleased(InputKey.Escape) || _layer.Input.IsKeyReleased(InputKey.Escape)) { _vm.ExecuteCancel(); return; }
            if (Input.IsKeyReleased(InputKey.Enter) || _layer.Input.IsKeyReleased(InputKey.Enter))
            { if (_vm.EntryVisible) _vm.ExecuteNext(); else if (_vm.CanAccept) _vm.ExecuteAccept(); }
        }

        public static void CloseFor(IReignActionHost owner) { if (_owner == owner) Close(); }

        public static void Close()
        {
            if (_layer != null)
            {
                _layer.IsFocusLayer = false; _layer.InputRestrictions.ResetInputRestrictions(); ScreenManager.TryLoseFocus(_layer);
                if (_screen?.HasLayer(_layer) == true) _screen.RemoveLayer(_layer);
            }
            _vm?.OnFinalize(); _vm = null; _layer = null; _owner = null; _screen = null;
            IsCalibrationFixture = false;
            if (_previousFocus != null) ScreenManager.TrySetFocus(_previousFocus);
            _previousFocus = null;
        }
    }
}

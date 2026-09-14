using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.UI
{
    internal static class ReignMainMenuVideoPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.main_menu_video";
        private const string ModuleId = "ReignBeta";
        private const string VideoFileName = "1440p_pc.ivf";

        private static Harmony _harmony;
        private static string _lastFallbackReason = string.Empty;
        private static bool _selectionLogged;

        internal static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }

            try
            {
                MethodInfo refreshScene = AccessTools.Method(typeof(MBInitialScreenBase), "RefreshScene");
                if (refreshScene == null)
                {
                    throw new MissingMethodException(typeof(MBInitialScreenBase).FullName, "RefreshScene");
                }

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    refreshScene,
                    transpiler: new HarmonyMethod(
                        typeof(ReignMainMenuVideoPatches),
                        nameof(UseReignVideoTranspiler)));
                ReignLog.Info("Reign main-menu visual override patch loaded; native menu audio remains enabled.");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Reign main-menu visual override patch failed; native menu videos remain enabled: " + ex);
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
            }
        }

        internal static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            _lastFallbackReason = string.Empty;
            _selectionLogged = false;
        }

        private static IEnumerable<CodeInstruction> UseReignVideoTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(
                typeof(VideoPlayerView),
                nameof(VideoPlayerView.PlayVideo),
                new[] { typeof(string), typeof(string), typeof(float), typeof(bool) });
            MethodInfo replacement = AccessTools.Method(
                typeof(ReignMainMenuVideoPatches),
                nameof(PlayReignVideoWithNativeAudio));
            if (original == null || replacement == null)
            {
                throw new MissingMethodException("Could not resolve the main-menu video playback hook.");
            }

            int replacementCount = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (Equals(instruction.operand, original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replacementCount++;
                }

                yield return instruction;
            }

            if (replacementCount != 1)
            {
                throw new InvalidOperationException(
                    "Expected one VideoPlayerView.PlayVideo call in MBInitialScreenBase.RefreshScene, found "
                    + replacementCount
                    + ".");
            }
        }

        private static void PlayReignVideoWithNativeAudio(
            VideoPlayerView player,
            string nativeVideoPath,
            string nativeAudioPath,
            float frameRate,
            bool looping)
        {
            string reignVideoPath = GetReignVideoPath();
            if (!IsCompatibleVideo(reignVideoPath))
            {
                LogFallbackOnce("the Reign VP8/IVF movie is missing or incompatible");
                player.PlayVideo(nativeVideoPath, nativeAudioPath, frameRate, looping);
                return;
            }

            _lastFallbackReason = string.Empty;
            if (!_selectionLogged)
            {
                _selectionLogged = true;
                ReignLog.Info(
                    "Using the Reign main-menu movie with native menu audio: "
                    + reignVideoPath
                    + " :: "
                    + nativeAudioPath);
            }

            player.PlayVideo(reignVideoPath, nativeAudioPath, frameRate, looping);
        }

        private static string GetReignVideoPath()
        {
            return System.IO.Path.Combine(
                BasePath.Name,
                "Modules",
                ModuleId,
                "Videos",
                "initial_menu",
                "reign",
                VideoFileName);
        }

        private static bool IsCompatibleVideo(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                byte[] header = new byte[32];
                using (FileStream stream = File.OpenRead(path))
                {
                    if (stream.Length <= header.Length || stream.Read(header, 0, header.Length) != header.Length)
                    {
                        return false;
                    }
                }

                return Encoding.ASCII.GetString(header, 0, 4) == "DKIF"
                    && Encoding.ASCII.GetString(header, 8, 4) == "VP80"
                    && BitConverter.ToUInt16(header, 12) == 2560
                    && BitConverter.ToUInt16(header, 14) == 1440
                    && BitConverter.ToUInt32(header, 16) == 24
                    && BitConverter.ToUInt32(header, 20) == 1
                    && BitConverter.ToUInt32(header, 24) > 0;
            }
            catch (Exception ex)
            {
                LogFallbackOnce("asset validation failed: " + ex.Message);
                return false;
            }
        }

        private static void LogFallbackOnce(string reason)
        {
            if (string.Equals(_lastFallbackReason, reason, StringComparison.Ordinal))
            {
                return;
            }

            _lastFallbackReason = reason;
            ReignLog.Warn("Reign main-menu movie unavailable because " + reason + "; using native menu video and audio.");
        }
    }
}

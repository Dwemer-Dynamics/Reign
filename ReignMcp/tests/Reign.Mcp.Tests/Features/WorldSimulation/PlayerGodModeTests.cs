using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class PlayerGodModeTests
{
    [ValidatedGodModeClientFact]
    public async Task LicensedNativeSetterPreservesOwnersAndWaitsForTheAnimationTable()
    {
        foreach (string mode in new[] { "Ownership", "UnsafeEarlyPatch", "AnimationStartup" })
        {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "player-god-mode-native", mode + "-" + Guid.NewGuid().ToString("N"));
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shellRoot, "powershell.exe")) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["PSModulePath"] = Path.Combine(shellRoot, "Modules");
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Features/WorldSimulation/PlayerGodModeProbe.ps1"),
            "-ClientAssembly", client, "-BannerlordPath", game, "-EvidenceDirectory", evidence, "-Mode", mode }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException("God-mode native hook probe timed out: " + evidence); }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        if (mode == "Ownership")
            foreach (string key in new[] { "hookBound", "idempotent", "otherOwnerPreserved", "nativeMortalityRoundTrip", "equalsKeyBound" })
                Assert.True(report.RootElement.GetProperty(key).GetBoolean(), key);
        }
    }

    private sealed class ValidatedGodModeClientFactAttribute : FactAttribute
    {
        public ValidatedGodModeClientFactAttribute()
        {
            if (MapBarInputSafetyTests.CurrentClientArtifact() == null || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires same-run canonical client artifact and licensed Bannerlord path.";
        }
    }

    [WindowsFact]
    public async Task ProductionMissionBehaviorScopesAndRestoresNativeInvulnerability()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/WorldSimulation/Combat/ReignPlayerGodMode.cs"));
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "player-god-mode", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "production.cs"), source);
        File.WriteAllText(Path.Combine(evidence, "probe.cs"), Harness);
        File.WriteAllText(Path.Combine(evidence, "probe.ps1"), """
            $ErrorActionPreference = 'Stop'
            Add-Type -Path @((Join-Path $PSScriptRoot 'production.cs'),(Join-Path $PSScriptRoot 'probe.cs'))
            $result = [PlayerGodModeProbe]::Run()
            $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'report.json')
            """ );
        var start = new ProcessStartInfo("pwsh") {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = evidence,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(evidence, "probe.ps1") })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException("Player god-mode probe timed out: " + evidence); }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(report.RootElement.GetProperty("checks").GetInt32() >= 20);
        string bootstrap = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Platform/SubModule.cs"));
        Assert.Contains("ReignBeta.WorldSimulation.Combat.ReignPlayerGodMode.Attach(mission);", bootstrap);
        Assert.Contains("ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.TryApply();", bootstrap);
        int earlyStart = bootstrap.IndexOf("protected override void OnSubModuleLoad()", StringComparison.Ordinal);
        int lateStart = bootstrap.IndexOf("protected override void OnBeforeInitialModuleScreenSetAsRoot()", StringComparison.Ordinal);
        Assert.DoesNotContain("ReignPlayerGodModeHealthPatch", bootstrap[earlyStart..lateStart]);
        Assert.Contains("ReignPlayerGodModeHealthPatch.TryApply();", bootstrap[lateStart..bootstrap.IndexOf("private static void EnsureManagedRuntimeDependencies", StringComparison.Ordinal)]);
        Assert.Contains("ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.Unapply();", bootstrap);
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Isolated mission source execution requires PowerShell on Windows."; }
    }

    // Execute the whole production behavior, replacing only native engine/input I/O.
    // Client compilation binds the real API; this fixture does not claim native combat proof.
    private const string Harness = """
        using System;
        using System.Collections.Generic;
        using TaleWorlds.Core;
        using TaleWorlds.InputSystem;
        using TaleWorlds.MountAndBlade;
        namespace HarmonyLib {
            public static class Priority { public const int Last=0; }
            public sealed class HarmonyMethod { public int priority; public HarmonyMethod(Type type,string name) {} }
            public sealed class Harmony {
                public static bool Fail; public static int Patches;
                public Harmony(string owner) {} public void Patch(System.Reflection.MethodInfo method,HarmonyMethod prefix) { if(Fail) throw new Exception("fixture hook failure"); Patches++; }
                public void UnpatchAll(string owner) {}
            }
            public static class AccessTools { public static System.Reflection.MethodInfo PropertySetter(Type type,string name) { return type.GetProperty(name).GetSetMethod(); } }
        }
        namespace ReignBeta.Platform.Integration { public static class ReignLog { public static void Exception(string message,Exception ex) {} } }
        namespace TaleWorlds.Core {
            public enum MissionMode { StartUp, Conversation, Battle, Duel, Stealth, Barter, Deployment, Tournament, Replay, CutScene, Benchmark }
            public sealed class InformationMessage { public InformationMessage(string text) {} }
        }
        namespace TaleWorlds.Library {
            public static class InformationManager { public static int Messages; public static void DisplayMessage(InformationMessage message) { Messages++; } }
        }
        namespace TaleWorlds.InputSystem {
            public enum InputKey { LeftAlt, RightAlt, LeftControl, RightControl, LeftShift, RightShift, Equals }
            public static class Input {
                public static HashSet<InputKey> Down = new HashSet<InputKey>(); public static bool Pressed;
                public static bool IsKeyDown(InputKey key) { return Down.Contains(key); }
                public static bool IsKeyPressed(InputKey key) { return Pressed && key == InputKey.Equals; }
            }
        }
        namespace TaleWorlds.MountAndBlade {
            public static class MBAnimation { public static int GetActionCodeWithName(string name) { return 1; } }
            public static class GameNetwork { public static bool IsMultiplayer; }
            public class MissionLogic {
                public Mission Mission; public virtual void OnMissionTick(float dt) {} protected virtual void OnEndMission() {}
                public virtual void OnRemoveBehavior() {} public void End() { OnEndMission(); }
            }
            public sealed class Mission {
                public static Mission Current; public Agent MainAgent; public MissionLogic Behavior; public MissionMode Mode = MissionMode.Battle;
                public int Added;
                public T GetMissionBehavior<T>() where T:class { return Behavior as T; }
                public void AddMissionBehavior(MissionLogic value) { Behavior=value; value.Mission=this; Added++; }
            }
            public sealed class Agent {
                public enum MortalityState { Mortal, Invulnerable, Immortal }
                public MortalityState CurrentMortalityState; public bool IsHuman = true, IsPlayerControlled = true, Active = true;
                public float Health { get; set; } = 37; public Agent MountAgent; public Mission Mission;
                public bool IsActive() { return Active; }
                public void SetMortalityState(MortalityState state) { CurrentMortalityState = state; }
            }
        }
        public static class PlayerGodModeProbe {
            private static int checks;
            private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
            private static void Key(ReignBeta.WorldSimulation.Combat.ReignPlayerGodMode behavior, bool alt, bool pressed) {
                Input.Down.Clear(); if (alt) Input.Down.Add(InputKey.LeftAlt); Input.Pressed = pressed; behavior.OnMissionTick(.016f);
            }
            public static object Run() {
                var player = new Agent(); var mount = new Agent { IsHuman = false }; player.MountAgent = mount;
                var npc = new Agent(); var mission = new Mission { MainAgent = player }; Mission.Current = mission;
                var mode = new ReignBeta.WorldSimulation.Combat.ReignPlayerGodMode { Mission = mission };
                mission.Behavior=mode; player.Mission=mission; mount.Mission=mission; npc.Mission=mission;
                var attachMission=new Mission(); Check(ReignBeta.WorldSimulation.Combat.ReignPlayerGodMode.Attach(attachMission),"attach once");
                Check(!ReignBeta.WorldSimulation.Combat.ReignPlayerGodMode.Attach(attachMission) && attachMission.Added==1,"duplicate attach");
                HarmonyLib.Harmony.Fail=true; Key(mode,true,true);
                Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"hook failure does not enable or escape tick");
                HarmonyLib.Harmony.Fail=false;
                Key(mode,false,true); Check(player.CurrentMortalityState == Agent.MortalityState.Mortal,"Equals alone");
                Key(mode,true,false); Check(player.CurrentMortalityState == Agent.MortalityState.Mortal,"Alt alone");
                Key(mode,true,true); Check(player.CurrentMortalityState == Agent.MortalityState.Invulnerable,"enable");
                Check(player.Health == 37,"must not heal"); Check(mount.CurrentMortalityState == Agent.MortalityState.Mortal,"mount");
                Check(npc.CurrentMortalityState == Agent.MortalityState.Mortal,"npc");
                float hp=0; ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.GuardHealth(player,ref hp); Check(hp==37,"direct scripted damage");
                hp=float.NaN; ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.GuardHealth(player,ref hp); Check(hp==37,"invalid damage");
                hp=50; ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.GuardHealth(player,ref hp); Check(hp==50,"healing unaffected");
                hp=37; ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.GuardHealth(player,ref hp); Check(hp==37,"equal health");
                hp=float.PositiveInfinity; ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.GuardHealth(player,ref hp); Check(float.IsPositiveInfinity(hp),"positive health assignment unchanged");
                hp=0; ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.GuardHealth(npc,ref hp); Check(hp==0,"npc direct damage");
                ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.GuardHealth(mount,ref hp); Check(hp==0,"mount direct damage");
                Key(mode,true,false); Check(player.CurrentMortalityState == Agent.MortalityState.Invulnerable,"held key");
                Check(HarmonyLib.Harmony.Patches==1,"idempotent hook");
                Key(mode,true,true); Check(player.CurrentMortalityState == Agent.MortalityState.Mortal,"disable");
                hp=0; ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.GuardHealth(player,ref hp); Check(hp==0,"damage restored when off");
                player.CurrentMortalityState = Agent.MortalityState.Immortal;
                Input.Down.Clear(); Input.Down.Add(InputKey.RightAlt); Input.Pressed=true; mode.OnMissionTick(.016f);
                Check(player.CurrentMortalityState == Agent.MortalityState.Invulnerable,"right alt");
                mode.OnRemoveBehavior(); Check(player.CurrentMortalityState == Agent.MortalityState.Immortal,"restore prior immortal");
                player.CurrentMortalityState=Agent.MortalityState.Invulnerable; Key(mode,true,true); Key(mode,true,true);
                Check(player.CurrentMortalityState==Agent.MortalityState.Invulnerable,"preserve prior invulnerability");
                player.CurrentMortalityState = Agent.MortalityState.Mortal; Key(mode,true,true); mode.End();
                Check(player.CurrentMortalityState == Agent.MortalityState.Mortal,"mission end");
                foreach(var m in new[] {MissionMode.Battle,MissionMode.Duel,MissionMode.Stealth,MissionMode.Tournament}) {
                    mission.Mode=m; Key(mode,true,true); Check(player.CurrentMortalityState==Agent.MortalityState.Invulnerable,"combat mode"); mode.OnRemoveBehavior();
                }
                foreach(var m in new[] {MissionMode.Conversation,MissionMode.CutScene,MissionMode.Deployment,MissionMode.Barter,MissionMode.Replay}) {
                    mission.Mode=m; Key(mode,true,true); Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"noncombat mode");
                }
                mission.Mode=MissionMode.Battle; Key(mode,true,true); var replacement=new Agent(); mission.MainAgent=replacement;
                Key(mode,false,false); Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"release old agent");
                Check(replacement.CurrentMortalityState==Agent.MortalityState.Mortal,"no transfer");
                mission.MainAgent=player; GameNetwork.IsMultiplayer=true; Key(mode,true,true);
                Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"multiplayer"); GameNetwork.IsMultiplayer=false;
                player.Health=0; Key(mode,true,true); Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"no revive"); player.Health=37;
                player.Health=float.NaN; Key(mode,true,true); Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"invalid health cannot enable"); player.Health=37;
                Key(mode,true,true); player.CurrentMortalityState=Agent.MortalityState.Immortal; mode.OnRemoveBehavior();
                Check(player.CurrentMortalityState==Agent.MortalityState.Immortal,"external owner");
                player.CurrentMortalityState=Agent.MortalityState.Mortal; Key(mode,true,true);
                int notices=TaleWorlds.Library.InformationManager.Messages; mission.Mode=MissionMode.Conversation; Key(mode,false,false);
                Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"leaving combat");
                Check(TaleWorlds.Library.InformationManager.Messages==notices+1,"involuntary release notice");
                mission.Mode=MissionMode.Duel; Key(mode,false,false); Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"no automatic reenable after conversation");
                mission.Mode=MissionMode.Battle; Input.Down.Clear(); Input.Down.Add(InputKey.LeftAlt); Input.Down.Add(InputKey.LeftControl); Input.Pressed=true;
                mode.OnMissionTick(.016f); Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"control chord");
                Input.Down.Remove(InputKey.LeftControl); Input.Down.Add(InputKey.LeftShift); mode.OnMissionTick(.016f);
                Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"shift chord");
                player.IsPlayerControlled=false; Key(mode,true,true); Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"spectator"); player.IsPlayerControlled=true;
                Key(mode,true,true); Mission.Current=null; Key(mode,false,false); Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"stale mission");
                Mission.Current=mission; Key(mode,true,true); ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.Unapply();
                Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"unload restores agent");
                mode.End(); Check(player.CurrentMortalityState==Agent.MortalityState.Mortal,"cleanup is idempotent after unload");
                hp=0; ReignBeta.WorldSimulation.Combat.ReignPlayerGodModeHealthPatch.GuardHealth(player,ref hp); Check(hp==0,"unload clears active cache");
                return new { ok=true, checks=checks };
            }
        }
        """;
}

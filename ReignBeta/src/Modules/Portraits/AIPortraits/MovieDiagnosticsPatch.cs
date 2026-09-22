using System;
using AIEventsAndIntrigue.Settings;
using HarmonyLib;
using ReignBeta.UI.Calibration;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;

namespace AIPortraits;

[HarmonyPatch(typeof(GauntletLayer), "LoadMovie", new Type[]
{
	typeof(string),
	typeof(ViewModel)
})]
public static class MovieDiagnosticsPatch
{
	private static void Postfix(GauntletLayer __instance, string movieName, ViewModel dataSource)
	{
		try
		{
			ReignUiCalibrationService.TryAttach(__instance, movieName);
			AIEventsSettings instance = AIEventsSettings.Instance;
			if (instance != null && instance.LogUIMovies)
			{
				string text = dataSource?.GetType().FullName ?? "null";
				string text2 = "[AIP-UI] movie='" + movieName + "'  vm='" + text + "'";
				InformationManager.DisplayMessage(new InformationMessage(text2, Color.FromUint(4284927231u)));
				Debug.Print(text2);
			}
		}
		catch
		{
		}
	}
}

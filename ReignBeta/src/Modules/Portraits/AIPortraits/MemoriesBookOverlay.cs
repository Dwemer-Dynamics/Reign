using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace AIPortraits;

public static class MemoriesBookOverlay
{
	private static ScreenBase _hostScreen;

	private static GauntletLayer _layer;

	private static GauntletMovieIdentifier _movie;

	private static MemoriesBookVM _vm;

	private static bool _calibrationMode;

	internal static bool IsOpen => _layer != null;

	internal static bool IsCalibrationOpen => _layer != null && _calibrationMode;

	public static void Open()
	{
		OpenInternal(false);
	}

	internal static void OpenForCalibration()
	{
		OpenInternal(true);
	}

	internal static bool CloseCalibrationFixture()
	{
		if (!IsCalibrationOpen)
		{
			return false;
		}
		Close();
		return true;
	}

	private static void OpenInternal(bool calibrationMode)
	{
		try
		{
			if (_layer != null)
			{
				Close();
			}
			ScreenBase topScreen = ScreenManager.TopScreen;
			if (topScreen == null)
			{
				Msg("Could not open Memories Book: no active screen.", 4294919168u);
				return;
			}
			MemoriesBookVM memoriesBookVM = new MemoriesBookVM(Close, calibrationMode);
			GauntletLayer gauntletLayer = new GauntletLayer("AIPortraitsMemoriesBookLayer", 400, shouldClear: true);
			gauntletLayer.InputRestrictions.SetInputRestrictions();
			GauntletMovieIdentifier movie = gauntletLayer.LoadMovie("AIPortraitsMemoriesBook", memoriesBookVM);
			_hostScreen = topScreen;
			_vm = memoriesBookVM;
			_layer = gauntletLayer;
			_movie = movie;
			_calibrationMode = calibrationMode;
			_hostScreen.AddLayer(_layer);
			ScreenManager.TrySetFocus(_layer);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Failed to open memories book overlay: " + ex);
			Close();
			Msg("Could not open Memories Book. See rgl_log.txt.", 4294919168u);
		}
	}

	public static void Close()
	{
		try
		{
			if (_layer != null && _movie != null)
			{
				_layer.ReleaseMovie(_movie);
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Failed to release memories book movie: " + ex.Message);
		}
		try
		{
			if (_hostScreen != null && _layer != null && _hostScreen.HasLayer(_layer))
			{
				_hostScreen.RemoveLayer(_layer);
			}
		}
		catch (Exception ex2)
		{
			Debug.Print("[AIPortraits] Failed to remove memories book layer: " + ex2.Message);
		}
		try
		{
			_vm?.OnFinalize();
		}
		catch (Exception ex3)
		{
			Debug.Print("[AIPortraits] Failed to finalize memories book VM: " + ex3.Message);
		}
		_movie = null;
		_layer = null;
		_vm = null;
		_hostScreen = null;
		_calibrationMode = false;
	}

	private static void Msg(string text, uint color)
	{
		InformationManager.DisplayMessage(new InformationMessage("[AIPortraits] " + text, Color.FromUint(color)));
	}
}

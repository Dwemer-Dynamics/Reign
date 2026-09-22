using System;
using System.IO;
using ReignBeta.UI.EventArt;
using TaleWorlds.Library;

namespace AIPortraits;

public sealed class MemoriesBookVM : ViewModel
{
	private readonly Action _close;

	private readonly bool _calibrationMode;

	private static readonly string[] CalibrationFiles =
	{
		"A Pact Proclaimed Before the Lords of Calradia.png",
		"The Council Supper at Zeonica.png",
		"A Quiet Road Beyond the Southern Gate.png"
	};

	private static readonly string[] CalibrationCaptions =
	{
		"A formal pact is proclaimed before the assembled lords of Calradia.",
		"The royal household gathers for a measured evening of counsel and remembrance.",
		"A quiet journey beyond the southern gate becomes a memory worth preserving."
	};

	private string[] _files;

	private readonly MBBindingList<MemoryBookItemVM> _memoryItems = new MBBindingList<MemoryBookItemVM>();

	private int _index;

	private string _imageId;

	private string _titleText;

	private string _captionText;

	private string _counterText;

	private bool _hasMemories;

	[DataSourceProperty]
	public MBBindingList<MemoryBookItemVM> MemoryItems => _memoryItems;

	[DataSourceProperty]
	public string MemoryBookImageId
	{
		get
		{
			return _imageId;
		}
		set
		{
			if (_imageId != value)
			{
				_imageId = value;
				OnPropertyChanged("MemoryBookImageId");
			}
		}
	}

	[DataSourceProperty]
	public string TitleText
	{
		get
		{
			return _titleText;
		}
		set
		{
			if (_titleText != value)
			{
				_titleText = value;
				OnPropertyChanged("TitleText");
			}
		}
	}

	[DataSourceProperty]
	public string CaptionText
	{
		get
		{
			return _captionText;
		}
		set
		{
			if (_captionText != value)
			{
				_captionText = value;
				OnPropertyChanged("CaptionText");
			}
		}
	}

	[DataSourceProperty]
	public string CounterText
	{
		get
		{
			return _counterText;
		}
		set
		{
			if (_counterText != value)
			{
				_counterText = value;
				OnPropertyChanged("CounterText");
			}
		}
	}

	[DataSourceProperty]
	public bool HasMemories
	{
		get
		{
			return _hasMemories;
		}
		set
		{
			if (_hasMemories != value)
			{
				_hasMemories = value;
				OnPropertyChanged("HasMemories");
			}
		}
	}

	public MemoriesBookVM(Action close)
		: this(close, false)
	{
	}

	internal MemoriesBookVM(Action close, bool calibrationMode)
	{
		_close = close;
		_calibrationMode = calibrationMode;
		PortraitPatch.SetMemoryBookIdentity(null);
		if (_calibrationMode)
		{
			_files = CalibrationFiles;
			RebuildMemoryItems();
			UpdateCurrent();
		}
		else
		{
			Refresh();
		}
	}

	public void ExecuteNext()
	{
		if (_files != null && _files.Length != 0)
		{
			_index = (_index + 1) % _files.Length;
			UpdateCurrent();
		}
	}

	public void ExecutePrevious()
	{
		if (_files != null && _files.Length != 0)
		{
			_index = (_index - 1 + _files.Length) % _files.Length;
			UpdateCurrent();
		}
	}

	public void ExecuteClose()
	{
		_close?.Invoke();
	}

	internal void SelectMemory(MemoryBookItemVM item)
	{
		if (item == null || _files == null || _files.Length == 0)
		{
			return;
		}
		for (int i = 0; i < _memoryItems.Count; i++)
		{
			if (_memoryItems[i] == item)
			{
				_index = i;
				UpdateCurrent();
				break;
			}
		}
	}

	private void Refresh()
	{
		_files = MemoryService.GetMemoryFiles();
		RebuildMemoryItems();
		_index = Math.Max(0, Math.Min(_index, _files.Length - 1));
		UpdateCurrent();
	}

	private void RebuildMemoryItems()
	{
		_memoryItems.Clear();
		if (_files != null)
		{
			for (int i = 0; i < _files.Length; i++)
			{
				_memoryItems.Add(new MemoryBookItemVM(this, _files[i], i));
			}
		}
	}

	private void UpdateCurrent()
	{
		if (_calibrationMode)
		{
			HasMemories = true;
			TitleText = "Memories Book";
			CaptionText = CalibrationCaptions[_index];
			CounterText = _index + 1 + " / " + _files.Length;
			MemoryBookImageId = ReignEventArtTextureFactory.BuildImageId(
				"feast_empire", "toasts_and_table_talk");
			UpdateSelectedItem();
			return;
		}

		HasMemories = _files != null && _files.Length != 0;
		if (!HasMemories)
		{
			TitleText = "Memories Book";
			CaptionText = "No memories have been created yet.";
			CounterText = "0 / 0";
			MemoryBookImageId = string.Empty;
			UpdateSelectedItem();
			return;
		}
		string text = _files[_index];
		MemoryBookImageId = ReignEventArtTextureFactory.BuildMemorySceneImageId(text);
		TitleText = "Memories Book";
		CaptionText = MemoryService.GetMemoryDescription(text);
		if (string.IsNullOrWhiteSpace(CaptionText))
		{
			CaptionText = Path.GetFileNameWithoutExtension(text);
		}
		CounterText = _index + 1 + " / " + _files.Length;
		UpdateSelectedItem();
	}

	private void UpdateSelectedItem()
	{
		for (int i = 0; i < _memoryItems.Count; i++)
		{
			_memoryItems[i].IsSelected = i == _index;
		}
	}

}

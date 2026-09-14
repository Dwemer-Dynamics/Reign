using System.IO;
using TaleWorlds.Library;

namespace AIPortraits;

public sealed class MemoryBookItemVM : ViewModel
{
	private readonly MemoriesBookVM _book;

	private bool _isSelected;

	public string Path { get; }

	public int Index { get; }

	[DataSourceProperty]
	public string ListText { get; }

	[DataSourceProperty]
	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			if (_isSelected != value)
			{
				_isSelected = value;
				OnPropertyChanged("IsSelected");
			}
		}
	}

	public MemoryBookItemVM(MemoriesBookVM book, string path, int index)
	{
		_book = book;
		Path = path;
		Index = index;
		string memoryDescription = MemoryService.GetMemoryDescription(path);
		ListText = (string.IsNullOrWhiteSpace(memoryDescription) ? System.IO.Path.GetFileNameWithoutExtension(path) : memoryDescription);
	}

	public void ExecuteSelect()
	{
		_book?.SelectMemory(this);
	}
}

using System.Collections.Concurrent;
using TaleWorlds.Library;

namespace AIPortraits;

public static class MemoryQueue
{
	private readonly struct ReadyMemory(string path, string title, bool deferForAIInfluenceResponse)
	{
		public readonly string Path = path;

		public readonly string Title = title;

		public readonly bool DeferForAIInfluenceResponse = deferForAIInfluenceResponse;
	}

	private static readonly ConcurrentQueue<ReadyMemory> _ready = new ConcurrentQueue<ReadyMemory>();

	public static void EnqueueReady(string path, string title, bool deferForAIInfluenceResponse = false)
	{
		_ready.Enqueue(new ReadyMemory(path, title, deferForAIInfluenceResponse));
	}

	public static void DrainOnMainThread()
	{
		ReadyMemory result;
		while (_ready.TryDequeue(out result))
		{
			MemoryService.MarkGenerationComplete();
			TextureFactory.Invalidate(MemoryService.ToMemoryKey(result.Path));
			InformationManager.DisplayMessage(new InformationMessage("[AIPortraits] Memory ready: " + result.Title, Color.FromUint(4278246775u)));
			ConversationPortraitMixin.ShowMemoryImageFromPatch(result.Path);
		}
	}
}

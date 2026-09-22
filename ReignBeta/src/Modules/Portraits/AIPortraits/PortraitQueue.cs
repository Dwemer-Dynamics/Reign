using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using ReignBeta.Integration;
using TaleWorlds.Library;

namespace AIPortraits;

public static class PortraitQueue
{
	private readonly struct ReadyItem(string id, string gamePath, ReignPortraitGenerationResult product)
	{
		public readonly string Id = id;

		public readonly string GamePath = gamePath;

		public readonly ReignPortraitGenerationResult Product = product;
	}

	private static readonly ConcurrentQueue<ReadyItem> _ready = new ConcurrentQueue<ReadyItem>();

	public static int PendingCount => _ready.Count;

	public static void EnqueueReady(string id, string gamePath, ReignPortraitGenerationResult product)
	{
		_ready.Enqueue(new ReadyItem(id, gamePath, product));
	}

	public static void DrainOnMainThread()
	{
		for (int i = 0; i < 3; i++)
		{
			if (!_ready.TryDequeue(out var result))
			{
				break;
			}
			try
			{
				_ = CompleteReadyAsync(result);
			}
			catch (Exception ex)
			{
				Debug.Print("[AIPortraits] DrainOnMainThread error for " + result.Id + ": " + ex.Message);
				PortraitCache.MarkComplete(result.Id);
			}
		}
	}

	private static async Task CompleteReadyAsync(ReadyItem result)
	{
		try
		{
			bool prepared = await PortraitCache.SavePortraitProductAsync(result.Id, result.Product).ConfigureAwait(false);
			if (prepared && !string.IsNullOrWhiteSpace(result.GamePath))
			{
				File.WriteAllBytes(result.GamePath, result.Product.ImageBytes);
			}
			await ReignMainThread.InvokeAsync(delegate
			{
				TextureFactory.Invalidate(result.Id);
				PortraitCache.MarkComplete(result.Id);
				string message = prepared
					? "[AIPortraits] Portrait ready! Reopen character screen to see it."
					: "[AIPortraits] Portrait was saved, but its display images could not be prepared.";
				InformationManager.DisplayMessage(new InformationMessage(message, Color.FromUint(prepared ? 4278246775u : 4294919168u)));
			}).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Portrait completion error for " + result.Id + ": " + ex.Message);
			await ReignMainThread.InvokeAsync(() => PortraitCache.MarkComplete(result.Id)).ConfigureAwait(false);
		}
	}
}

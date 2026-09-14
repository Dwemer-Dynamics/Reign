using System;
using AIEventsAndIntrigue.Settings;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIPortraits;

public static class MemoryService
{
	private const string MemoryKeyPrefix = "memory:";

	private static volatile bool _generationRunning;

	private static bool _migrationChecked;

	private static string[] LegacyMemoriesDirs => new[]
	{
		Path.Combine(BasePath.Name, "Modules", "AIPortraits", "Memories"),
		Path.Combine(BasePath.Name, "Modules", "AIEventsAndIntrigue", "Memories")
	};

	public static string MemoriesDir => Path.Combine(BasePath.Name, "Modules", "BannerlordReign", "Memories");

	public static string PromptPicturesDir => Path.Combine(MemoriesDir, "PromptPictures");

	public static void EnsureDirectory()
	{
		Directory.CreateDirectory(MemoriesDir);
		Directory.CreateDirectory(PromptPicturesDir);
		MigrateLegacyMemoriesIfNeeded();
	}

	public static string ToMemoryKey(string path)
	{
		return "memory:" + (path ?? string.Empty);
	}

	public static bool IsMemoryKey(string key)
	{
		if (!string.IsNullOrWhiteSpace(key))
		{
			return key.StartsWith("memory:", StringComparison.Ordinal);
		}
		return false;
	}

	public static string PathFromMemoryKey(string key)
	{
		if (!IsMemoryKey(key))
		{
			return null;
		}
		return key.Substring("memory:".Length);
	}

	public static byte[] GetMemoryBytes(string key)
	{
		string text = PathFromMemoryKey(key);
		if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
		{
			return null;
		}
		try
		{
			return File.ReadAllBytes(text);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Failed to read memory image: " + ex.Message);
			return null;
		}
	}

	public static bool Exists(string key)
	{
		string text = PathFromMemoryKey(key);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return File.Exists(text);
		}
		return false;
	}

	public static bool TryGetAspectRatio(string key, out float ratio)
	{
		ratio = 0f;
		byte[] memoryBytes = GetMemoryBytes(key);
		if (memoryBytes == null || memoryBytes.Length == 0)
		{
			return false;
		}
		byte[] array;
		int width;
		int height;
		if (IsJpeg(memoryBytes))
		{
			byte[] png = JpegToPng.Convert(memoryBytes);
			array = PngReencode.DecodeToRgba(png, out width, out height);
		}
		else
		{
			array = PngReencode.DecodeToRgba(memoryBytes, out width, out height);
		}
		if (array == null || width <= 0 || height <= 0)
		{
			return false;
		}
		ratio = (float)width / (float)height;
		return ratio > 0f;
	}

	public static string[] GetMemoryFiles()
	{
		EnsureDirectory();
		string[] files = Directory.GetFiles(MemoriesDir, "*.png", SearchOption.TopDirectoryOnly);
		string[] files2 = Directory.GetFiles(MemoriesDir, "*.jpg", SearchOption.TopDirectoryOnly);
		string[] files3 = Directory.GetFiles(MemoriesDir, "*.jpeg", SearchOption.TopDirectoryOnly);
		string[] array = new string[files.Length + files2.Length + files3.Length];
		files.CopyTo(array, 0);
		files2.CopyTo(array, files.Length);
		files3.CopyTo(array, files.Length + files2.Length);
		int newSize = 0;
		for (int i = 0; i < array.Length; i++)
		{
			if (IsBookMemoryImage(array[i]))
			{
				array[newSize++] = array[i];
			}
		}
		Array.Resize(ref array, newSize);
		Array.Sort(array, (IComparer<string>)StringComparer.OrdinalIgnoreCase);
		return array;
	}

	public static string GetMemoryDescription(string memoryPath)
	{
		if (string.IsNullOrWhiteSpace(memoryPath))
		{
			return string.Empty;
		}
		string path = Path.ChangeExtension(memoryPath, ".txt");
		if (!File.Exists(path))
		{
			return string.Empty;
		}
		try
		{
			string[] array = File.ReadAllLines(path);
			foreach (string text in array)
			{
				if (text.StartsWith("Description: ", StringComparison.OrdinalIgnoreCase))
				{
					return text.Substring("Description: ".Length).Trim();
				}
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Failed to read memory description: " + ex.Message);
		}
		return string.Empty;
	}

	public static void OpenMemoriesBook()
	{
		try
		{
			if (GetMemoryFiles().Length == 0)
			{
				Msg("No memories have been created yet.", 4294945280u);
			}
			else
			{
				MemoriesBookOverlay.Open();
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Failed to open memories book: " + ex);
			Msg("Could not open Memories Book. See rgl_log.txt.", 4294919168u);
		}
	}

	public static void CreateMemory(Hero npcHero, string description, bool deferDisplayUntilAIInfluenceResponse = false)
	{
		try
		{
			AIEventsSettings instance = AIEventsSettings.Instance;
			if (instance != null && !instance.ModEnabled)
			{
				Msg("AI Portraits is disabled.", 4294945280u);
				return;
			}
			if (_generationRunning)
			{
				Msg("A memory is already being painted. Please wait.", 4294945280u);
				return;
			}
			if (Hero.MainHero == null || npcHero == null)
			{
				Msg("A memory needs both you and the conversation character.", 4294919168u);
				return;
			}
			description = (description ?? string.Empty).Trim();
			if (description.Length < 3)
			{
				Msg("Describe what happened first.", 4294945280u);
				return;
			}
			string id = CharacterCacheId.ForHero(Hero.MainHero);
			string id2 = CharacterCacheId.ForHero(npcHero);
			byte[] diskBytes = PortraitCache.GetDiskBytes(id);
			byte[] diskBytes2 = PortraitCache.GetDiskBytes(id2);
			if (diskBytes == null || diskBytes2 == null)
			{
				Msg("Both characters need AI portraits before creating a memory.", 4294945280u);
				return;
			}
			byte[] reference = MemoryReferenceComposer.ComposeTwoPortraitReference(diskBytes, diskBytes2);
			if (reference == null)
			{
				Msg("Could not build the two-character reference image.", 4294919168u);
				return;
			}
			EnsureDirectory();
			_generationRunning = true;
			string text = Hero.MainHero.Name?.ToString() ?? "Player";
			string text2 = npcHero.Name?.ToString() ?? "Companion";
			string title = text + " and " + text2;
			string text3 = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + CharacterCacheId.Sanitize(text + "_and_" + text2);
			string memoryPath = Path.Combine(MemoriesDir, text3 + ".png");
			string path = Path.Combine(MemoriesDir, text3 + ".txt");
			string referencePath = Path.Combine(PromptPicturesDir, text3 + "_reference.png");
			string prompt = BuildMemoryPrompt(text, text2, description);
			File.WriteAllBytes(referencePath, reference);
			File.WriteAllText(path, "Title: " + title + Environment.NewLine + "Description: " + description + Environment.NewLine + "Prompt: " + prompt + Environment.NewLine + "Reference: temporary image deleted after generation" + Environment.NewLine, Encoding.UTF8);
			Msg("Painting memory of " + text2 + "...", 4278233855u);
			Task.Run(async delegate
			{
				try
				{
					byte[] array = await NanoGptClient.GenerateMemoryImageAsync(prompt, reference, "1024x768", npcHero.StringId, PortraitPromptContext.FromHero(npcHero));
					DeleteTemporaryReference(referencePath);
					if (array == null || array.Length == 0)
					{
						MarkGenerationComplete();
						Msg("Memory generation failed: " + (NanoGptClient.LastErrorForDisplay ?? "check the Bannerlord Reign server image settings."), 4294919168u);
					}
					else
					{
						File.WriteAllBytes(memoryPath, array);
						MemoryQueue.EnqueueReady(memoryPath, title, deferDisplayUntilAIInfluenceResponse);
					}
				}
				catch (Exception ex2)
				{
					DeleteTemporaryReference(referencePath);
					MarkGenerationComplete();
					Debug.Print("[AIPortraits] Memory generation error: " + ex2);
					Msg("Memory generation errored. See rgl_log.txt.", 4294919168u);
				}
			});
		}
		catch (Exception ex)
		{
			MarkGenerationComplete();
			Debug.Print("[AIPortraits] CreateMemory error: " + ex);
			Msg("Could not start memory generation.", 4294919168u);
		}
	}

	public static void MarkGenerationComplete()
	{
		_generationRunning = false;
	}

	private static string BuildMemoryPrompt(string playerName, string npcName, string description)
	{
		return "Use the attached reference sheet: the left portrait is " + playerName + ", and the right portrait is " + npcName + ". Create one coherent realistic scene showing both people together. Preserve both identities, facial structures, ages, hairstyles, facial hair, skin tones, and overall likenesses from the reference. Do not merge them into one person, change their genders, modernize their clothing, or replace their faces. The scene should be historically grounded for Mount & Blade Bannerlord: medieval or ancient civilian clothing, natural materials, no modern objects. Show this memory: " + description + ". Make it cinematic, detailed, natural, and emotionally believable.";
	}

	private static bool IsJpeg(byte[] bytes)
	{
		if (bytes != null && bytes.Length >= 3 && bytes[0] == byte.MaxValue && bytes[1] == 216)
		{
			return bytes[2] == byte.MaxValue;
		}
		return false;
	}

	private static bool IsBookMemoryImage(string path)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		if (!string.IsNullOrWhiteSpace(fileNameWithoutExtension))
		{
			return !fileNameWithoutExtension.EndsWith("_reference", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static void DeleteTemporaryReference(string path)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Failed to delete temporary memory reference: " + ex.Message);
		}
	}

	private static void Msg(string text, uint color)
	{
		InformationManager.DisplayMessage(new InformationMessage("[AIPortraits] " + text, Color.FromUint(color)));
	}

	private static void MigrateLegacyMemoriesIfNeeded()
	{
		if (_migrationChecked)
		{
			return;
		}

		_migrationChecked = true;
		try
		{
			bool migrated = false;
			foreach (string legacyMemoriesDir in LegacyMemoriesDirs)
			{
				if (!Directory.Exists(legacyMemoriesDir))
				{
					continue;
				}

				CopyDirectory(legacyMemoriesDir, MemoriesDir);
				migrated = true;
			}

			if (migrated)
			{
				Debug.Print("[AIPortraits] Migrated memories path to BannerlordReign module folder.");
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Memories migration skipped: " + ex.Message);
		}
	}

	private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
	{
		Directory.CreateDirectory(destinationDirectory);
		foreach (string sourceFile in Directory.GetFiles(sourceDirectory))
		{
			string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
			if (!File.Exists(destinationFile))
			{
				File.Copy(sourceFile, destinationFile);
			}
		}

		foreach (string sourceChild in Directory.GetDirectories(sourceDirectory))
		{
			string destinationChild = Path.Combine(destinationDirectory, Path.GetFileName(sourceChild));
			CopyDirectory(sourceChild, destinationChild);
		}
	}
}

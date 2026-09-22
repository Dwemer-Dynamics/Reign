using Newtonsoft.Json;

namespace AIPortraits;

public class NanoGptResponse
{
	[JsonProperty("data")]
	public NanoGptImage[] Data { get; set; }

	[JsonProperty("cost")]
	public float Cost { get; set; }
}

using Newtonsoft.Json;

namespace AIPortraits;

public class NanoGptImage
{
	[JsonProperty("b64_json")]
	public string B64Json { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }
}

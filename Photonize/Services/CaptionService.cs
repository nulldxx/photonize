using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Photonize.Models;

namespace Photonize.Services;

/// <summary>
/// Generates text captions for images using a Florence-2 workflow running on a
/// ComfyUI server, and writes each caption alongside its source image with a
/// ".txt" extension (e.g. holiday/duck.webp -> holiday/duck.txt).
///
/// This is a C# port of the "comfy-getcaption" Python script. The image is
/// uploaded to the server, the embedded Florence-2 workflow is run against it,
/// and the resulting caption is read back over the API from the ShowText node.
/// </summary>
public class CaptionService
{
    /// <summary>Default ComfyUI server hosting the Florence-2 model.</summary>
    public const string DefaultServer = "mordor:8000";

    /// <summary>Image extensions we know how to caption.</summary>
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"
    };

    // Node whose "image" input names the uploaded file to caption.
    private const string LoadImageNode = "40";

    // Embedded Florence-2 captioning workflow in ComfyUI API format. Node 40
    // (LoadImage) has its "image" input rewritten per-image before submission;
    // the caption is recovered from node 29 (ShowText) in the execution history.
    private const string WorkflowTemplateJson = """
    {
      "26": {
        "inputs": {
          "text_input": "",
          "task": "more_detailed_caption",
          "fill_mask": true,
          "keep_model_loaded": true,
          "max_new_tokens": 1024,
          "num_beams": 3,
          "do_sample": true,
          "output_mask_select": "",
          "seed": 959170552143510,
          "image": ["40", 0],
          "florence2_model": ["28", 0]
        },
        "class_type": "Florence2Run",
        "_meta": { "title": "Florence2Run" }
      },
      "28": {
        "inputs": {
          "model": "thwri/CogFlorence-2.1-Large",
          "precision": "fp16",
          "convert_to_safetensors": "sdpa"
        },
        "class_type": "DownloadAndLoadFlorence2Model",
        "_meta": { "title": "DownloadAndLoadFlorence2Model" }
      },
      "29": {
        "inputs": {
          "text_0": "",
          "text": ["26", 2]
        },
        "class_type": "ShowText|pysssss",
        "_meta": { "title": "Show Text 🐍" }
      },
      "40": {
        "inputs": { "image": "images.webp" },
        "class_type": "LoadImage",
        "_meta": { "title": "Load Image" }
      }
    }
    """;

    private readonly string _serverAddress;
    private readonly string _clientId = Guid.NewGuid().ToString();

    public CaptionService(string? serverAddress = null)
    {
        _serverAddress = string.IsNullOrWhiteSpace(serverAddress) ? DefaultServer : serverAddress.Trim();
    }

    /// <summary>
    /// Generate captions for a list of images, writing a sibling .txt file for each.
    /// </summary>
    /// <param name="photos">Images to caption.</param>
    /// <param name="overwriteCallback">
    /// Called with the list of images whose .txt already exists. Returns how to
    /// proceed. If null, existing captions are overwritten.
    /// </param>
    /// <param name="progress">Progress reporter (current, total, status).</param>
    /// <param name="timeoutSeconds">Maximum wait per image, in seconds.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<(bool Success, string Message)> GenerateCaptionsAsync(
        List<PhotoItem> photos,
        Func<List<string>, OverwriteOption>? overwriteCallback = null,
        IProgress<(int current, int total, string status)>? progress = null,
        int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        if (photos == null || photos.Count == 0)
        {
            return (false, "No images to caption.");
        }

        // Restrict to real image files we can caption.
        var targets = photos
            .Where(p => !p.IsFolder && !p.IsVideo && SupportedExtensions.Contains(Path.GetExtension(p.FilePath)))
            .ToList();

        if (targets.Count == 0)
        {
            return (false, "No captionable images in the selection.");
        }

        // Ask about overwriting captions that already exist.
        var existing = targets
            .Where(p => File.Exists(Path.ChangeExtension(p.FilePath, ".txt")))
            .Select(p => Path.GetFileNameWithoutExtension(p.FilePath) + ".txt")
            .ToList();

        OverwriteOption overwriteOption = OverwriteOption.OverwriteAll;
        if (existing.Count > 0 && overwriteCallback != null)
        {
            overwriteOption = overwriteCallback(existing);
            if (overwriteOption == OverwriteOption.Cancel)
            {
                return (false, "Auto-caption cancelled by user.");
            }
        }

        using var httpClient = new HttpClient();

        int captioned = 0;
        int skipped = 0;
        var failures = new List<string>();
        int total = targets.Count;
        int processed = 0;

        progress?.Report((0, total, $"Captioning 0 of {total}..."));

        foreach (var photo in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputPath = Path.ChangeExtension(photo.FilePath, ".txt");

            if (overwriteOption == OverwriteOption.SkipExisting && File.Exists(outputPath))
            {
                skipped++;
                processed++;
                progress?.Report((processed, total, $"Skipped {photo.FileName}"));
                continue;
            }

            progress?.Report((processed, total, $"Captioning {photo.FileName}..."));

            try
            {
                var caption = await CaptionImageAsync(httpClient, photo.FilePath, timeoutSeconds, cancellationToken);
                await File.WriteAllTextAsync(outputPath, caption + "\n", System.Text.Encoding.UTF8, cancellationToken);
                captioned++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{photo.FileName}: {ex.Message}");
            }

            processed++;
            progress?.Report((processed, total, $"Captioned {processed} of {total}"));
        }

        progress?.Report((total, total, "Complete"));

        var parts = new List<string>();
        if (captioned > 0) parts.Add($"{captioned} captioned");
        if (skipped > 0) parts.Add($"{skipped} skipped");
        if (failures.Count > 0) parts.Add($"{failures.Count} failed");

        var message = parts.Count > 0
            ? $"Auto-caption complete: {string.Join(", ", parts)} of {total} image(s)."
            : "No captions were generated.";

        if (failures.Count > 0)
        {
            message += $"\n\nFailed:\n{string.Join("\n", failures)}";
        }

        return (captioned > 0, message);
    }

    /// <summary>Run the Florence workflow for one image and return its caption text.</summary>
    private async Task<string> CaptionImageAsync(
        HttpClient httpClient, string imagePath, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var serverName = await UploadImageAsync(httpClient, imagePath, cancellationToken);

        var workflow = JObject.Parse(WorkflowTemplateJson);
        workflow[LoadImageNode]!["inputs"]!["image"] = serverName;

        var promptId = await SubmitWorkflowAsync(httpClient, workflow, cancellationToken);
        var history = await PollStatusAsync(httpClient, promptId, timeoutSeconds, cancellationToken);

        var caption = ExtractCaption(history);
        if (string.IsNullOrEmpty(caption))
        {
            throw new InvalidOperationException("Workflow completed but no caption text was returned.");
        }

        return caption;
    }

    /// <summary>
    /// Upload an image to the ComfyUI server's input folder. Returns the
    /// server-side name to reference from a LoadImage node (which may differ from
    /// the local filename if the server disambiguates collisions).
    /// </summary>
    private async Task<string> UploadImageAsync(
        HttpClient httpClient, string imagePath, CancellationToken cancellationToken)
    {
        var url = $"http://{_serverAddress}/upload/image";

        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue(GuessMimeType(imagePath));
        form.Add(fileContent, "image", Path.GetFileName(imagePath));
        form.Add(new StringContent("true"), "overwrite");

        using var response = await httpClient.PostAsync(url, form, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JObject.Parse(json);

        var name = result.Value<string>("name");
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException($"Unexpected upload response: {json}");
        }

        var subfolder = result.Value<string>("subfolder");
        return string.IsNullOrEmpty(subfolder) ? name : $"{subfolder}/{name}";
    }

    /// <summary>Submit a workflow to the ComfyUI server, returning the prompt id.</summary>
    private async Task<string> SubmitWorkflowAsync(
        HttpClient httpClient, JObject workflow, CancellationToken cancellationToken)
    {
        var url = $"http://{_serverAddress}/prompt";
        var payload = new JObject
        {
            ["prompt"] = workflow,
            ["client_id"] = _clientId
        };

        using var content = new StringContent(payload.ToString(), System.Text.Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Error submitting workflow ({(int)response.StatusCode}): {body}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JObject.Parse(json);
        var promptId = result.Value<string>("prompt_id");
        if (string.IsNullOrEmpty(promptId))
        {
            throw new InvalidOperationException($"Unexpected submit response: {json}");
        }

        return promptId;
    }

    /// <summary>Poll workflow execution history until completion or timeout.</summary>
    private async Task<JObject> PollStatusAsync(
        HttpClient httpClient, string promptId, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var url = $"http://{_serverAddress}/history/{promptId}";
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out after {timeoutSeconds} seconds waiting for caption.");
            }

            try
            {
                using var response = await httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var history = JObject.Parse(json);

                if (history[promptId] is JObject promptData)
                {
                    if (promptData["outputs"] != null)
                    {
                        return promptData;
                    }

                    var statusStr = promptData["status"]?["status_str"]?.Value<string>();
                    if (statusStr == "error")
                    {
                        var messages = promptData["status"]?["messages"]?.ToString() ?? "Unknown error";
                        throw new InvalidOperationException($"Execution error: {messages}");
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Transient polling error; retry until the timeout elapses.
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    /// <summary>
    /// Pull the caption text out of completed workflow history. The ShowText node
    /// reports its text back under a "text" key; we scan every output node for it.
    /// </summary>
    private static string? ExtractCaption(JObject promptData)
    {
        if (promptData["outputs"] is not JObject outputs)
        {
            return null;
        }

        foreach (var node in outputs.Properties())
        {
            if (node.Value is JObject nodeOutput && nodeOutput["text"] is JToken textToken)
            {
                string text = textToken is JArray parts
                    ? string.Join("\n", parts.Select(p => p.ToString()))
                    : textToken.ToString();

                text = text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string GuessMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };
}

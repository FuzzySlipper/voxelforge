using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxelForge.App.Console;

/// <summary>
/// JSON-line protocol over stdin/stdout for external LLM agents.
/// One JSON object per line in, one per line out.
///
/// Input:  {"command": "set", "args": ["0", "0", "0", "1"]}
///   or:   {"command": "describe"}
/// Output: {"ok": true, "message": "Set (0,0,0) = 1"}
///   or:   {"ok": true, "message": "Captured viewport", "image": "base64..."}
///   or:   {"ok": true, "message": "Captured 5 views", "images": ["base64...", ...]}
/// </summary>
public sealed class StdioHost
{
    private readonly CommandRouter _router;
    private readonly CommandContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public StdioHost(CommandRouter router, CommandContext context)
    {
        _router = router;
        _context = context;
    }

    /// <summary>
    /// Read JSON-line commands from stdin, write results to stdout.
    /// Blocks the calling thread. Call from a background thread.
    /// </summary>
    public void Run(CancellationToken ct)
    {
        WriteOutput(new StdioCommandResponse { Ok = true, Message = "VoxelForge stdio ready. Send JSON commands." });

        using var reader = new StreamReader(System.Console.OpenStandardInput());

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = reader.ReadLine();
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            StdioCommandResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<StdioCommandRequest>(line, JsonOptions);
                if (request?.Command is null)
                {
                    response = new StdioCommandResponse { Ok = false, Message = "Missing 'command' field." };
                }
                else
                {
                    var args = request.Args ?? [];
                    var result = _router.Execute(request.Command, args, _context);
                    response = new StdioCommandResponse
                    {
                        Ok = result.Success,
                        Message = result.Message,
                    };

                    // Attach image data if present
                    if (result.Data is byte[] imageBytes)
                    {
                        response.Image = Convert.ToBase64String(imageBytes);
                    }
                    else if (result.Data is byte[][] imageArray)
                    {
                        response.Images = imageArray.Select(Convert.ToBase64String).ToArray();
                    }
                }
            }
            catch (JsonException ex)
            {
                response = new StdioCommandResponse { Ok = false, Message = $"Invalid JSON: {ex.Message}" };
            }

            WriteOutput(response);
        }
    }

    private static void WriteOutput(StdioCommandResponse response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        System.Console.Out.WriteLine(json);
        System.Console.Out.Flush();
    }

}

using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.AI;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dishhive.Api.Services.Import;

public interface IRecipeImageCapability
{
    string State { get; }
    Task<string> CheckAsync(CancellationToken cancellationToken, bool force = false);
}

public sealed class NoOpRecipeImageCapability : IRecipeImageCapability
{
    public string State => "unavailable";
    public Task<string> CheckAsync(CancellationToken cancellationToken, bool force = false) => Task.FromResult(State);
}

/// <summary>Independent image-input evidence; never uses a planning-quality verdict.</summary>
public sealed class RecipeImageCapability(IAiExecutionGate gate, AiOptions options) : IRecipeImageCapability
{
    private readonly SemaphoreSlim _mutex = new(1);
    private sealed record Cached(string Fingerprint, string State, DateTime Expires);
    private volatile Cached? _cached;
    public string State => _cached is { } cached && cached.Fingerprint == options.ExecutionFingerprint
        && cached.Expires > DateTime.UtcNow ? cached.State : "notChecked";
    internal enum ProbeColor { Red, Blue, Green, Yellow, Orange, Purple, Black, White }
    internal sealed record ProbeResult(ProbeColor[] Colors);
    internal static ChatResponseFormat Format => AiJsonContract.CreateResponseFormat<ProbeResult>("image_capability");

    public async Task<string> CheckAsync(CancellationToken cancellationToken, bool force = false)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (!force && State != "notChecked") return State;
            var (bytes, colors) = CreateProbe();
            var operationId = $"vision-{Guid.NewGuid():N}";
            using var operation = gate.BeginOperation(cancellationToken, operationId, independent: true);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "Identify the colors of the four solid tiles in the image, ordered top-left, top-right, bottom-left, bottom-right. Reply only in this JSON shape: "
                    + AiJsonContract.Serialize(new ProbeResult([ProbeColor.Red, ProbeColor.Blue, ProbeColor.Green, ProbeColor.Yellow]))),
                new(ChatRole.User, [new TextContent("Read the image; do not infer colors from the example."), new DataContent(bytes, "image/png")])
            };
            string state;
            try
            {
                var result = await gate.ExecuteValidatedAsync(new AiExecutionRequest(AiTaskKind.CapabilityVision,
                    messages, "vision-capability-v1", operationId, ResponseFormat: Format, CapabilityProbe: true), response =>
                {
                    ProbeResult? parsed;
                    try { parsed = AiJsonContract.Deserialize<ProbeResult>(response.Text); }
                    catch (JsonException) { return AiValidation<ProbeResult>.Invalid(AiTaskOutcome.ParseFailure, "Invalid image capability response"); }
                    return parsed?.Colors is { } actual && actual.SequenceEqual(colors)
                        ? AiValidation<ProbeResult>.Valid(parsed)
                        : AiValidation<ProbeResult>.Invalid(AiTaskOutcome.ValidationFailure, "Image content was not identified correctly");
                }, cancellationToken);
                state = result.Succeeded ? "available" : "unsupported";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { state = "unavailable"; }
            var expiry = state == "available" ? DateTime.UtcNow.AddHours(options.CapabilitySuccessHours)
                : DateTime.UtcNow.AddSeconds(options.CapabilityFailureCooldownSeconds);
            _cached = new(options.ExecutionFingerprint, state, expiry);
            return state;
        }
        finally { _mutex.Release(); }
    }

    internal static (byte[] Bytes, ProbeColor[] Colors) CreateProbe()
    {
        var remaining = Enum.GetValues<ProbeColor>().ToList();
        var colors = Enumerable.Range(0, 4).Select(_ =>
        {
            var index = RandomNumberGenerator.GetInt32(remaining.Count);
            var color = remaining[index]; remaining.RemoveAt(index); return color;
        }).ToArray();
        using var bitmap = new SKBitmap(128, 128);
        using var canvas = new SKCanvas(bitmap);
        for (var i = 0; i < colors.Length; i++)
        {
            using var paint = new SKPaint { Color = colors[i] switch
            {
                ProbeColor.Red => SKColors.Red, ProbeColor.Blue => SKColors.Blue, ProbeColor.Green => SKColors.Green,
                ProbeColor.Yellow => SKColors.Yellow, ProbeColor.Orange => SKColors.Orange, ProbeColor.Purple => SKColors.Purple,
                ProbeColor.Black => SKColors.Black, _ => SKColors.White
            } };
            canvas.DrawRect((i % 2) * 64, (i / 2) * 64, 64, 64, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return (data.ToArray(), colors);
    }
}

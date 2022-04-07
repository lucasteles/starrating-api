using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

async Task<Image<Rgba32>> LoadImage(string path, StarEnum star) =>
    await Image.LoadAsync<Rgba32>(Path.Combine(path, "Images",
        star switch
        {
            StarEnum.Filled => "star-fill.png",
            StarEnum.Blank => "star-blank.png",
            _ => throw new ArgumentOutOfRangeException(nameof(star))
        }));

async Task<MemoryStream> GenerateImage(string localPath, Parameters param)
{
    using var filled = await LoadImage(localPath, StarEnum.Filled);
    using var blank = await LoadImage(localPath, StarEnum.Blank);
    using var outputImage = new Image<Rgba32>((blank.Width + param.Space) * param.Count - param.Space, blank.Height);
    
    outputImage.Mutate(output =>
    {
        for (var i = 0; i < param.Count; i++)
        {
            var pos = new Point(i * (blank.Width + param.Space) , 0);
            output.DrawImage(blank, pos, 1f);

            var ratingToDraw = param.Rate - i;
            if (ratingToDraw <= 0) continue;

            if (ratingToDraw >= 1)
            {
                output.DrawImage(filled, pos, 1f);
                continue;
            }
            
            using var partial = filled.Clone();
            partial.Mutate(o =>
                o.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.TopLeft,
                    Size = new((int)(filled.Width * ratingToDraw), filled.Height)
                }));
            output.DrawImage(partial, pos, 1f);
        }

        if (param.Scale != 1)
        {
            var newSize = output.GetCurrentSize() * param.Scale;
            output.Resize((Size)newSize);
        }
        
    });
    
    MemoryStream memory = new();
    await outputImage.SaveAsync(memory, new PngEncoder());
    return memory;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMemoryCache();
var app = builder.Build();

Parameters NormalizeParams(float? rate, float? scale, int? space, int? count)
{
    var clampedNumberOfStars = Math.Clamp(count ?? 5, 1, 10);
    var clampedRate = Math.Clamp(rate ?? 0f, 0f, clampedNumberOfStars);
    var clampedScale = Math.Clamp(scale ?? 1, 0.1f, 10f);
    var clampedSpace = Math.Clamp(space ?? 0, 0, 100);
    return new(clampedRate, clampedSpace, clampedScale, clampedNumberOfStars);
}

app.MapGet("/stars", async (
    IWebHostEnvironment env,
    IMemoryCache memoryCache,
    float? rate, int? space, float? scale, int? count) =>
{
    var parameters = NormalizeParams(rate, scale, space, count);

    var imageBytes = await memoryCache.GetOrCreateAsync(
        parameters.GetHashCode(),
        async entry =>
        {
            await using var stream = await GenerateImage(env.ContentRootPath, parameters);
            entry.SlidingExpiration = TimeSpan.FromMinutes(4);
            return stream.ToArray();
        });
    
    return Results.File(imageBytes, "image/png");
});

app.Run();

public record Parameters(float Rate, int Space, float Scale, int Count);

public enum StarEnum { Filled, Blank }

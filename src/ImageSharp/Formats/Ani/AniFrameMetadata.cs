// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats.Ico;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Ani;

/// <summary>
/// Provides Ani specific metadata information for the image.
/// </summary>
public class AniFrameMetadata : IFormatFrameMetadata<AniFrameMetadata>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AniFrameMetadata"/> class.
    /// </summary>
    public AniFrameMetadata()
    {
    }

    /// <summary>
    /// Gets or sets the display time for this frame (in 1/60 seconds)
    /// </summary>
    public uint Rate { get; set; }

    /// <inheritdoc/>
    public static AniFrameMetadata FromFormatConnectingFrameMetadata(FormatConnectingFrameMetadata metadata) =>
        new()
        {
            Rate = (uint)metadata.Duration.TotalSeconds * 60
        };

    /// <inheritdoc/>
    IDeepCloneable IDeepCloneable.DeepClone() => new AniFrameMetadata { Rate = this.Rate };

    /// <inheritdoc/>
    public FormatConnectingFrameMetadata ToFormatConnectingFrameMetadata() => new FormatConnectingFrameMetadata() { Duration = TimeSpan.FromSeconds(this.Rate / 60d) };

    /// <inheritdoc/>
    public void AfterFrameApply<TPixel>(ImageFrame<TPixel> source, ImageFrame<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public AniFrameMetadata DeepClone() => new() { Rate = this.Rate };
}

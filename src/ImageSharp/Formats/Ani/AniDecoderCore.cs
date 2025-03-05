// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Cur;
using SixLabors.ImageSharp.Formats.Ico;
using SixLabors.ImageSharp.Formats.Icon;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Memory.Internals;
using SixLabors.ImageSharp.Metadata;

namespace SixLabors.ImageSharp.Formats.Ani;

internal class AniDecoderCore : ImageDecoderCore
{
    /// <summary>
    /// The general decoder options.
    /// </summary>
    private readonly Configuration configuration;

    /// <summary>
    /// The stream to decode from.
    /// </summary>
    private BufferedReadStream currentStream = null!;

    private AniHeader header;

    public AniDecoderCore(DecoderOptions options)
        : base(options) => this.configuration = options.Configuration;

    protected override Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        this.currentStream = stream;

        Guard.IsTrue(this.currentStream.TryReadUnmanaged(out RiffOrListChunkHeader riffHeader), nameof(riffHeader), "Invalid RIFF header.");
        long dataSize = riffHeader.Size;
        long dataStartPosition = this.currentStream.Position;

        if (!this.TryReadChunk(dataStartPosition, dataSize, out RiffChunkHeader riffChunkHeader) ||
            (AniChunkType)riffChunkHeader.FourCc is not AniChunkType.AniH)
        {
            Guard.IsTrue(false, nameof(riffChunkHeader), "Missing ANIH chunk.");
        }

        ImageMetadata metadata = new();
        AniMetadata aniMetadata = metadata.GetAniMetadata();

        if (this.currentStream.TryReadUnmanaged(out AniHeader result))
        {
            this.header = result;
            aniMetadata.Width = result.Width;
            aniMetadata.Height = result.Height;
            aniMetadata.BitCount = result.BitCount;
            aniMetadata.Planes = result.Planes;
            aniMetadata.DisplayRate = result.DisplayRate;
            aniMetadata.Flags = result.Flags;
        }

        Image<TPixel>? image = null;
        List<Image> frames = [];
        Span<uint> sequence = default;
        Span<uint> rate = default;

        try
        {
            while (this.TryReadChunk(dataStartPosition, dataSize, out RiffChunkHeader chunk))
            {
                switch ((AniChunkType)chunk.FourCc)
                {
                    case AniChunkType.Seq:
                    {
                        using IMemoryOwner<byte> data = this.ReadChunkData(chunk.Size);
                        sequence = MemoryMarshal.Cast<byte, uint>(data.Memory.Span);
                        break;
                    }

                    case AniChunkType.Rate:
                    {
                        using IMemoryOwner<byte> data = this.ReadChunkData(chunk.Size);
                        rate = MemoryMarshal.Cast<byte, uint>(data.Memory.Span);
                        break;
                    }

                    case AniChunkType.List:
                        HandleListChunk();
                        break;
                    default:
                        break;
                }
            }
        }
        catch
        {
            image?.Dispose();
            throw;
        }

        throw new NotImplementedException();

        void HandleListChunk()
        {
            if (!this.currentStream.TryReadUnmanaged(out uint listType))
            {
                return;
            }

            switch ((AniListType)listType)
            {
                case AniListType.Fram:
                {
                    while (this.TryReadChunk(dataStartPosition, dataSize, out RiffChunkHeader chunk))
                    {
                        if ((AniListFrameType)chunk.FourCc == AniListFrameType.Icon)
                        {
                            long endPosition = this.currentStream.Position + chunk.Size;
                            if (aniMetadata.Flags.HasFlag(AniHeaderFlags.IsIcon))
                            {
                                if (this.currentStream.TryReadUnmanaged(out IconDir dir))
                                {
                                    this.currentStream.Position -= Unsafe.SizeOf<IconDir>();

                                    switch (dir.Type)
                                    {
                                        case IconFileType.CUR:
                                            frames.Add(CurDecoder.Instance.Decode(this.Options,
                                                this.currentStream));
                                            break;
                                        case IconFileType.ICO:
                                            frames.Add(IcoDecoder.Instance.Decode(this.Options,
                                                this.currentStream));
                                            break;
                                    }
                                }
                            }
                            else
                            {
                                frames.Add(BmpDecoder.Instance.Decode(this.Options, this.currentStream));
                            }

                            this.currentStream.Position = endPosition;
                        }
                    }

                    break;
                }

                case AniListType.Info:
                {
                    while (this.TryReadChunk(dataStartPosition, dataSize, out RiffChunkHeader chunk))
                    {
                        switch ((AniListInfoType)chunk.FourCc)
                        {
                            case AniListInfoType.INam:
                            {
                                using IMemoryOwner<byte> data = this.ReadChunkData(chunk.Size);
                                aniMetadata.Name = Encoding.ASCII.GetString(data.Memory.Span).TrimEnd('\0');
                                break;
                            }

                            case AniListInfoType.IArt:
                            {
                                using IMemoryOwner<byte> data = this.ReadChunkData(chunk.Size);
                                aniMetadata.Artist = Encoding.ASCII.GetString(data.Memory.Span).TrimEnd('\0');
                                break;
                            }

                            default:
                                break;
                        }
                    }

                    break;
                }
            }
        }
    }

    private bool TryReadChunk(long startPosition, long size, out RiffChunkHeader chunk)
    {
        if (this.currentStream.Position - startPosition >= size)
        {
            chunk = default;
            return false;
        }

        return this.currentStream.TryReadUnmanaged(out chunk);
    }

    protected override ImageInfo Identify(BufferedReadStream stream, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Reads the chunk data from the stream.
    /// </summary>
    /// <param name="length">The length of the chunk data to read.</param>
    [MethodImpl(InliningOptions.ShortMethod)]
    private IMemoryOwner<byte> ReadChunkData(uint length)
    {
        if (length is 0)
        {
            return new BasicArrayBuffer<byte>([]);
        }

        // We rent the buffer here to return it afterwards in Decode()
        // We don't want to throw a degenerated memory exception here as we want to allow partial decoding
        // so limit the length.
        int len = (int)Math.Min(length, this.currentStream.Length - this.currentStream.Position);
        IMemoryOwner<byte> buffer = this.configuration.MemoryAllocator.Allocate<byte>(len, AllocationOptions.Clean);

        this.currentStream.Read(buffer.GetSpan(), 0, len);

        return buffer;
    }
}

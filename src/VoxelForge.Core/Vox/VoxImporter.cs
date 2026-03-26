using System.Text;
using Microsoft.Extensions.Logging;

namespace VoxelForge.Core.Vox;

/// <summary>
/// Imports MagicaVoxel .vox files (RIFF-based binary format).
/// </summary>
public sealed class VoxImporter
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<VoxImporter> _logger;

    public VoxImporter(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<VoxImporter>();
    }

    public IReadOnlyList<VoxelModel> Import(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        // Validate magic: "VOX " (4 bytes)
        var magic = new string(reader.ReadChars(4));
        if (magic != "VOX ")
            throw new InvalidDataException($"Not a .vox file: expected 'VOX ', got '{magic}'");

        int version = reader.ReadInt32();
        _logger.LogDebug("Importing .vox file, version {Version}", version);

        // Read MAIN chunk
        var models = new List<VoxelModel>();
        RgbaColor[]? palette = null;
        var pendingSizes = new Queue<(int SizeX, int SizeY, int SizeZ)>();

        ReadChunks(reader, stream.Length, models, ref palette, pendingSizes);

        // Apply palette to all models
        if (palette is not null)
        {
            foreach (var model in models)
            {
                for (int i = 1; i < 256; i++)
                {
                    var color = palette[i];
                    if (color.R != 0 || color.G != 0 || color.B != 0 || color.A != 0)
                    {
                        model.Palette.Set((byte)i, new MaterialDef
                        {
                            Name = $"color_{i}",
                            Color = color,
                        });
                    }
                }
            }
        }

        _logger.LogDebug("Imported {Count} model(s)", models.Count);
        return models;
    }

    private void ReadChunks(
        BinaryReader reader,
        long streamLength,
        List<VoxelModel> models,
        ref RgbaColor[]? palette,
        Queue<(int, int, int)> pendingSizes)
    {
        while (reader.BaseStream.Position < streamLength - 12) // Minimum chunk header size
        {
            if (reader.BaseStream.Position + 4 > streamLength) break;

            var chunkId = new string(reader.ReadChars(4));
            int contentSize = reader.ReadInt32();
            int childrenSize = reader.ReadInt32();

            long contentStart = reader.BaseStream.Position;

            switch (chunkId)
            {
                case "MAIN":
                    // MAIN has no content, just children
                    break;

                case "SIZE":
                    int sizeX = reader.ReadInt32();
                    int sizeY = reader.ReadInt32();
                    int sizeZ = reader.ReadInt32();
                    pendingSizes.Enqueue((sizeX, sizeY, sizeZ));
                    break;

                case "XYZI":
                    int numVoxels = reader.ReadInt32();
                    var model = new VoxelModel(_loggerFactory.CreateLogger<VoxelModel>());

                    if (pendingSizes.Count > 0)
                    {
                        var (sx, sy, sz) = pendingSizes.Dequeue();
                        model.GridHint = Math.Max(sx, Math.Max(sy, sz));
                    }

                    for (int i = 0; i < numVoxels; i++)
                    {
                        byte x = reader.ReadByte();
                        byte y = reader.ReadByte();
                        byte z = reader.ReadByte();
                        byte colorIndex = reader.ReadByte();
                        // .vox uses Y-up with Z as depth; we keep the same coords
                        model.SetVoxel(new Point3(x, z, y), colorIndex);
                    }

                    models.Add(model);
                    break;

                case "RGBA":
                    palette = new RgbaColor[256];
                    // .vox palette: 255 entries (index 1-255), index 0 is reserved
                    for (int i = 1; i <= 255; i++)
                    {
                        byte r = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte b = reader.ReadByte();
                        byte a = reader.ReadByte();
                        palette[i] = new RgbaColor(r, g, b, a);
                    }
                    // Read the last padding entry (index 0 placeholder)
                    reader.ReadBytes(4);
                    break;

                default:
                    // Skip unknown chunks
                    _logger.LogTrace("Skipping unknown chunk '{ChunkId}' ({Size} bytes)", chunkId, contentSize);
                    reader.BaseStream.Seek(contentStart + contentSize, SeekOrigin.Begin);
                    break;
            }

            // Ensure we've consumed all content bytes
            long expectedEnd = contentStart + contentSize;
            if (reader.BaseStream.Position < expectedEnd)
                reader.BaseStream.Seek(expectedEnd, SeekOrigin.Begin);

            // Children are read as subsequent chunks (flat in the stream after MAIN)
            // The childrenSize field is handled by the overall loop
        }
    }
}

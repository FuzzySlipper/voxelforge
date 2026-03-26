using System.Text;
using Microsoft.Extensions.Logging;

namespace VoxelForge.Core.Vox;

/// <summary>
/// Exports a VoxelModel to MagicaVoxel .vox format.
/// Labels and animation data are stripped (not representable in .vox).
/// </summary>
public sealed class VoxExporter
{
    private readonly ILogger<VoxExporter> _logger;

    public VoxExporter(ILogger<VoxExporter> logger)
    {
        _logger = logger;
    }

    public void Export(VoxelModel model, Stream stream)
    {
        _logger.LogInformation("Exporting to .vox format. Labels and animation data will be stripped.");

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Compute model bounds for SIZE chunk
        var bounds = model.GetBounds();
        int sizeX = 1, sizeY = 1, sizeZ = 1;
        if (bounds is not null)
        {
            sizeX = bounds.Value.Max.X - bounds.Value.Min.X + 1;
            sizeY = bounds.Value.Max.Z - bounds.Value.Min.Z + 1; // swap Y/Z for .vox
            sizeZ = bounds.Value.Max.Y - bounds.Value.Min.Y + 1;
        }

        // Build XYZI data
        var voxelData = new MemoryStream();
        using (var voxWriter = new BinaryWriter(voxelData, Encoding.ASCII, leaveOpen: true))
        {
            int minX = bounds?.Min.X ?? 0;
            int minY = bounds?.Min.Y ?? 0;
            int minZ = bounds?.Min.Z ?? 0;

            voxWriter.Write(model.GetVoxelCount());
            foreach (var (pos, paletteIndex) in model.Voxels)
            {
                voxWriter.Write((byte)(pos.X - minX));
                voxWriter.Write((byte)(pos.Z - minZ)); // swap Y/Z for .vox
                voxWriter.Write((byte)(pos.Y - minY));
                voxWriter.Write(paletteIndex);
            }
        }

        // Build RGBA data (256 entries × 4 bytes = 1024)
        var rgbaData = new byte[1024];
        for (int i = 1; i <= 255; i++)
        {
            var mat = model.Palette.Get((byte)i);
            int offset = (i - 1) * 4;
            if (mat is not null)
            {
                rgbaData[offset] = mat.Color.R;
                rgbaData[offset + 1] = mat.Color.G;
                rgbaData[offset + 2] = mat.Color.B;
                rgbaData[offset + 3] = mat.Color.A;
            }
        }

        // Compute chunk sizes
        int sizeContentSize = 12;
        int xyziContentSize = (int)voxelData.Length;
        int rgbaContentSize = 1024;

        int sizeChunkTotal = 12 + sizeContentSize;
        int xyziChunkTotal = 12 + xyziContentSize;
        int rgbaChunkTotal = 12 + rgbaContentSize;

        int mainChildrenSize = sizeChunkTotal + xyziChunkTotal + rgbaChunkTotal;

        // Write file
        // Magic + version
        writer.Write("VOX ".ToCharArray());
        writer.Write(150); // version

        // MAIN chunk
        WriteChunkHeader(writer, "MAIN", 0, mainChildrenSize);

        // SIZE chunk
        WriteChunkHeader(writer, "SIZE", sizeContentSize, 0);
        writer.Write(sizeX);
        writer.Write(sizeY);
        writer.Write(sizeZ);

        // XYZI chunk
        WriteChunkHeader(writer, "XYZI", xyziContentSize, 0);
        writer.Write(voxelData.ToArray());

        // RGBA chunk
        WriteChunkHeader(writer, "RGBA", rgbaContentSize, 0);
        writer.Write(rgbaData);
    }

    private static void WriteChunkHeader(BinaryWriter writer, string id, int contentSize, int childrenSize)
    {
        writer.Write(id.ToCharArray());
        writer.Write(contentSize);
        writer.Write(childrenSize);
    }
}

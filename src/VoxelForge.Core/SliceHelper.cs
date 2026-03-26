namespace VoxelForge.Core;

/// <summary>
/// Pure math helpers for 2D slice views. No engine dependencies.
/// </summary>
public static class SliceHelper
{
    /// <summary>
    /// Given a slice axis and layer, get the two UV axis indices for the 2D view.
    /// Returns (uAxis, vAxis) where 0=X, 1=Y, 2=Z.
    /// </summary>
    public static (int UAxis, int VAxis) GetSliceAxes(SliceAxis axis)
    {
        return axis switch
        {
            SliceAxis.X => (1, 2), // Y horizontal, Z vertical
            SliceAxis.Y => (0, 2), // X horizontal, Z vertical
            SliceAxis.Z => (0, 1), // X horizontal, Y vertical
            _ => (0, 1),
        };
    }

    /// <summary>
    /// Convert a 2D grid position (u, v) on a slice back to a 3D Point3.
    /// </summary>
    public static Point3 SliceToWorld(SliceAxis axis, int layer, int u, int v)
    {
        return axis switch
        {
            SliceAxis.X => new Point3(layer, u, v),
            SliceAxis.Y => new Point3(u, layer, v),
            SliceAxis.Z => new Point3(u, v, layer),
            _ => new Point3(u, v, layer),
        };
    }

    /// <summary>
    /// Convert a screen pixel position to a 2D grid cell, given the cell size and view offset.
    /// Returns null if outside the grid.
    /// </summary>
    public static (int U, int V)? PixelToCell(int pixelX, int pixelY, int cellSize, int offsetX, int offsetY, int gridSize)
    {
        int relX = pixelX - offsetX;
        int relY = pixelY - offsetY;

        if (relX < 0 || relY < 0)
            return null;

        int u = relX / cellSize;
        int v = relY / cellSize;

        if (u >= gridSize || v >= gridSize)
            return null;

        return (u, v);
    }

    /// <summary>
    /// Get the voxel at a given (u, v) position on the slice.
    /// </summary>
    public static byte? GetSliceVoxel(VoxelModel model, SliceAxis axis, int layer, int u, int v)
    {
        var pos = SliceToWorld(axis, layer, u, v);
        return model.GetVoxel(pos);
    }
}

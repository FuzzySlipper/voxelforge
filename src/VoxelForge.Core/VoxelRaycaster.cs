namespace VoxelForge.Core;

/// <summary>
/// Casts a ray through a voxel grid using the Amanatides & Woo DDA algorithm.
/// Pure math — no engine dependencies.
/// </summary>
public static class VoxelRaycaster
{
    /// <summary>
    /// Cast a ray through a VoxelModel.
    /// </summary>
    /// <param name="model">The voxel model to test against.</param>
    /// <param name="originX">Ray origin X.</param>
    /// <param name="originY">Ray origin Y.</param>
    /// <param name="originZ">Ray origin Z.</param>
    /// <param name="dirX">Ray direction X (does not need to be normalized).</param>
    /// <param name="dirY">Ray direction Y.</param>
    /// <param name="dirZ">Ray direction Z.</param>
    /// <param name="maxDistance">Maximum distance to search.</param>
    /// <returns>The hit result, or null if no voxel was hit.</returns>
    public static RaycastHit? Cast(
        VoxelModel model,
        float originX, float originY, float originZ,
        float dirX, float dirY, float dirZ,
        float maxDistance = 200f)
    {
        // Normalize direction
        float len = MathF.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        if (len < 1e-10f) return null;
        dirX /= len;
        dirY /= len;
        dirZ /= len;

        // Current voxel cell
        int x = (int)MathF.Floor(originX);
        int y = (int)MathF.Floor(originY);
        int z = (int)MathF.Floor(originZ);

        // Step direction
        int stepX = dirX >= 0 ? 1 : -1;
        int stepY = dirY >= 0 ? 1 : -1;
        int stepZ = dirZ >= 0 ? 1 : -1;

        // tDelta: how far along the ray to cross one cell in each axis
        float tDeltaX = dirX != 0 ? MathF.Abs(1f / dirX) : float.MaxValue;
        float tDeltaY = dirY != 0 ? MathF.Abs(1f / dirY) : float.MaxValue;
        float tDeltaZ = dirZ != 0 ? MathF.Abs(1f / dirZ) : float.MaxValue;

        // tMax: distance to the next cell boundary in each axis
        float tMaxX = dirX != 0
            ? ((stepX > 0 ? (x + 1f) - originX : originX - x) * tDeltaX)
            : float.MaxValue;
        float tMaxY = dirY != 0
            ? ((stepY > 0 ? (y + 1f) - originY : originY - y) * tDeltaY)
            : float.MaxValue;
        float tMaxZ = dirZ != 0
            ? ((stepZ > 0 ? (z + 1f) - originZ : originZ - z) * tDeltaZ)
            : float.MaxValue;

        float t = 0;
        int faceNx = 0, faceNy = 0, faceNz = 0;

        // Step through grid
        for (int i = 0; i < 1000 && t < maxDistance; i++)
        {
            // Check current cell
            var pos = new Point3(x, y, z);
            if (model.GetVoxel(pos) is not null)
            {
                return new RaycastHit(pos, new Point3(faceNx, faceNy, faceNz), t);
            }

            // Advance to next cell boundary
            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ)
                {
                    t = tMaxX;
                    x += stepX;
                    tMaxX += tDeltaX;
                    faceNx = -stepX; faceNy = 0; faceNz = 0;
                }
                else
                {
                    t = tMaxZ;
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                    faceNx = 0; faceNy = 0; faceNz = -stepZ;
                }
            }
            else
            {
                if (tMaxY < tMaxZ)
                {
                    t = tMaxY;
                    y += stepY;
                    tMaxY += tDeltaY;
                    faceNx = 0; faceNy = -stepY; faceNz = 0;
                }
                else
                {
                    t = tMaxZ;
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                    faceNx = 0; faceNy = 0; faceNz = -stepZ;
                }
            }
        }

        return null;
    }
}

namespace VoxelForge.Core.Reference;

/// <summary>
/// CPU-side skeletal animation: interpolates keyframes, computes bone transforms,
/// and skins vertex positions/normals. Engine-agnostic (plain floats, no FNA types).
/// </summary>
public static class SkeletalAnimator
{
    /// <summary>
    /// Compute the final bone matrices (world * inverseBindMatrix) for every bone at the given time.
    /// </summary>
    public static float[][] ComputeBoneMatrices(Skeleton skeleton, SkeletalAnimationClip clip, float timeSeconds)
    {
        int count = skeleton.BoneCount;
        var localTransforms = new float[count][];
        var worldTransforms = new float[count][];
        var finalMatrices = new float[count][];

        // Step 1: Compute local transform per bone (animated or bind pose)
        for (int i = 0; i < count; i++)
        {
            var channel = clip.FindChannel(i);
            if (channel is not null && channel.Keyframes.Length > 0)
            {
                localTransforms[i] = InterpolateKeyframes(channel, timeSeconds);
            }
            else
            {
                localTransforms[i] = skeleton.Bones[i].LocalBindTransform;
            }
        }

        // Step 2: Walk hierarchy to compute world transforms (parent * local)
        for (int i = 0; i < count; i++)
        {
            int parent = skeleton.Bones[i].ParentIndex;
            if (parent < 0)
            {
                worldTransforms[i] = localTransforms[i];
            }
            else
            {
                worldTransforms[i] = Mul4x4(worldTransforms[parent], localTransforms[i]);
            }
        }

        // Step 3: Multiply by inverse bind matrix to get final skinning matrix
        for (int i = 0; i < count; i++)
        {
            finalMatrices[i] = Mul4x4(worldTransforms[i], skeleton.Bones[i].InverseBindMatrix);
        }

        return finalMatrices;
    }

    /// <summary>
    /// Apply skinning to a set of vertices using precomputed bone matrices.
    /// Writes deformed positions and normals into the output arrays.
    /// </summary>
    public static void SkinVertices(
        ReferenceVertex[] vertices,
        float[][] boneMatrices,
        float[] outPositions,   // length = vertices.Length * 3
        float[] outNormals)     // length = vertices.Length * 3
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            ref readonly var v = ref vertices[i];

            float px = 0, py = 0, pz = 0;
            float nx = 0, ny = 0, nz = 0;

            ApplyBoneInfluence(v.BoneIndex0, v.BoneWeight0, v, boneMatrices, ref px, ref py, ref pz, ref nx, ref ny, ref nz);
            ApplyBoneInfluence(v.BoneIndex1, v.BoneWeight1, v, boneMatrices, ref px, ref py, ref pz, ref nx, ref ny, ref nz);
            ApplyBoneInfluence(v.BoneIndex2, v.BoneWeight2, v, boneMatrices, ref px, ref py, ref pz, ref nx, ref ny, ref nz);
            ApplyBoneInfluence(v.BoneIndex3, v.BoneWeight3, v, boneMatrices, ref px, ref py, ref pz, ref nx, ref ny, ref nz);

            int o = i * 3;
            outPositions[o] = px;
            outPositions[o + 1] = py;
            outPositions[o + 2] = pz;

            // Normalize the normal
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 1e-6f)
            {
                nx /= len;
                ny /= len;
                nz /= len;
            }

            outNormals[o] = nx;
            outNormals[o + 1] = ny;
            outNormals[o + 2] = nz;
        }
    }

    private static void ApplyBoneInfluence(
        int boneIndex, float weight,
        in ReferenceVertex v, float[][] boneMatrices,
        ref float px, ref float py, ref float pz,
        ref float nx, ref float ny, ref float nz)
    {
        if (weight <= 0 || boneIndex < 0 || boneIndex >= boneMatrices.Length)
            return;

        var m = boneMatrices[boneIndex];

        // Transform position (affine: multiply by 4x4, w=1)
        px += weight * (m[0] * v.PosX + m[1] * v.PosY + m[2] * v.PosZ + m[3]);
        py += weight * (m[4] * v.PosX + m[5] * v.PosY + m[6] * v.PosZ + m[7]);
        pz += weight * (m[8] * v.PosX + m[9] * v.PosY + m[10] * v.PosZ + m[11]);

        // Transform normal (rotation only, no translation: w=0)
        nx += weight * (m[0] * v.NormX + m[1] * v.NormY + m[2] * v.NormZ);
        ny += weight * (m[4] * v.NormX + m[5] * v.NormY + m[6] * v.NormZ);
        nz += weight * (m[8] * v.NormX + m[9] * v.NormY + m[10] * v.NormZ);
    }

    /// <summary>
    /// Interpolate a bone's keyframes at the given time to produce a local 4x4 transform.
    /// </summary>
    private static float[] InterpolateKeyframes(BoneAnimationChannel channel, float time)
    {
        var keyframes = channel.Keyframes;
        if (keyframes.Length == 1)
            return ComposeMatrix(keyframes[0]);

        // Clamp time to clip range
        if (time <= keyframes[0].Time)
            return ComposeMatrix(keyframes[0]);
        if (time >= keyframes[^1].Time)
            return ComposeMatrix(keyframes[^1]);

        int idx = channel.FindKeyframeIndex(time);
        var k0 = keyframes[idx];
        var k1 = keyframes[idx + 1];

        float dt = k1.Time - k0.Time;
        float t = dt > 0 ? (time - k0.Time) / dt : 0;

        // Lerp position
        float px = k0.PosX + (k1.PosX - k0.PosX) * t;
        float py = k0.PosY + (k1.PosY - k0.PosY) * t;
        float pz = k0.PosZ + (k1.PosZ - k0.PosZ) * t;

        // Slerp rotation (quaternion)
        Slerp(k0.RotX, k0.RotY, k0.RotZ, k0.RotW,
              k1.RotX, k1.RotY, k1.RotZ, k1.RotW,
              t, out float rx, out float ry, out float rz, out float rw);

        // Lerp scale
        float sx = k0.ScaleX + (k1.ScaleX - k0.ScaleX) * t;
        float sy = k0.ScaleY + (k1.ScaleY - k0.ScaleY) * t;
        float sz = k0.ScaleZ + (k1.ScaleZ - k0.ScaleZ) * t;

        return ComposeMatrix(px, py, pz, rx, ry, rz, rw, sx, sy, sz);
    }

    private static float[] ComposeMatrix(BoneKeyframe k)
        => ComposeMatrix(k.PosX, k.PosY, k.PosZ, k.RotX, k.RotY, k.RotZ, k.RotW, k.ScaleX, k.ScaleY, k.ScaleZ);

    /// <summary>
    /// Compose a 4x4 row-major matrix from TRS components.
    /// Order: Scale * Rotation * Translation (matches Assimp convention).
    /// </summary>
    private static float[] ComposeMatrix(
        float px, float py, float pz,
        float qx, float qy, float qz, float qw,
        float sx, float sy, float sz)
    {
        // Rotation matrix from quaternion
        float x2 = qx + qx, y2 = qy + qy, z2 = qz + qz;
        float xx = qx * x2, xy = qx * y2, xz = qx * z2;
        float yy = qy * y2, yz = qy * z2, zz = qz * z2;
        float wx = qw * x2, wy = qw * y2, wz = qw * z2;

        return
        [
            (1 - yy - zz) * sx,  (xy + wz) * sx,      (xz - wy) * sx,      0,
            (xy - wz) * sy,      (1 - xx - zz) * sy,   (yz + wx) * sy,      0,
            (xz + wy) * sz,      (yz - wx) * sz,       (1 - xx - yy) * sz,  0,
            px,                   py,                    pz,                   1,
        ];
    }

    /// <summary>
    /// Quaternion spherical linear interpolation.
    /// </summary>
    private static void Slerp(
        float ax, float ay, float az, float aw,
        float bx, float by, float bz, float bw,
        float t,
        out float rx, out float ry, out float rz, out float rw)
    {
        float dot = ax * bx + ay * by + az * bz + aw * bw;

        // If dot is negative, negate one quaternion to take the shorter arc
        if (dot < 0)
        {
            bx = -bx; by = -by; bz = -bz; bw = -bw;
            dot = -dot;
        }

        float s0, s1;
        if (dot > 0.9995f)
        {
            // Very close — use linear interpolation to avoid division by near-zero
            s0 = 1 - t;
            s1 = t;
        }
        else
        {
            float angle = MathF.Acos(dot);
            float sinAngle = MathF.Sin(angle);
            s0 = MathF.Sin((1 - t) * angle) / sinAngle;
            s1 = MathF.Sin(t * angle) / sinAngle;
        }

        rx = s0 * ax + s1 * bx;
        ry = s0 * ay + s1 * by;
        rz = s0 * az + s1 * bz;
        rw = s0 * aw + s1 * bw;
    }

    /// <summary>
    /// Multiply two 4x4 row-major matrices: result = A * B.
    /// </summary>
    private static float[] Mul4x4(float[] a, float[] b)
    {
        var r = new float[16];
        for (int row = 0; row < 4; row++)
        {
            int ri = row * 4;
            for (int col = 0; col < 4; col++)
            {
                r[ri + col] =
                    a[ri + 0] * b[col] +
                    a[ri + 1] * b[4 + col] +
                    a[ri + 2] * b[8 + col] +
                    a[ri + 3] * b[12 + col];
            }
        }
        return r;
    }
}

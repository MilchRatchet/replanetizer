// Copyright (C) 2018-2026, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;

namespace LibReplanetizer.Models
{
    public enum CollisionGeometryCategory : byte
    {
        None = 0,
        Standard = 1,
        Hero = 2,
        Unknown = 3,
        MobyTriangle = 4,
        MobyPrimitive = 5
    }

    public static class CollisionVertexMetadata
    {
        public static float Pack(byte collisionType, CollisionGeometryCategory category)
        {
            uint value = collisionType | ((uint) category << 8);
            return BitConverter.UInt32BitsToSingle(value);
        }

        public static byte GetCollisionType(float metadata)
        {
            return (byte) (BitConverter.SingleToUInt32Bits(metadata) & 0xFF);
        }

        public static CollisionGeometryCategory GetCategory(float metadata)
        {
            return (CollisionGeometryCategory) ((BitConverter.SingleToUInt32Bits(metadata) >> 8) & 0xFF);
        }
    }
}

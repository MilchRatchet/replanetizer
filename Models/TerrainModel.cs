﻿using System;
using System.Collections.Generic;
using System.IO;
using RatchetEdit.Headers;
using static RatchetEdit.DataFunctions;


namespace RatchetEdit.Models
{
    public class TerrainModel : Model
    {
        public List<uint> off_0C = new List<uint>();    // Something to do with lighting
        int faceCount;
        public TerrainModel(Decoder decoder, FileStream fs, TerrainHead head, byte[] tfragBlock, int num)
        {
            int offset = num * 0x30;
            int texturePointer = decoder.Int(tfragBlock, offset + 0x10);
            int textureCount = decoder.Int(tfragBlock, offset + 0x14);
            ushort vertexIndex = decoder.Ushort(tfragBlock, offset + 0x18);
            ushort vertexCount = decoder.Ushort(tfragBlock, offset + 0x1A);
            ushort slotNum = decoder.Ushort(tfragBlock, offset + 0x22);

            // Oh yes, we are hacking
            int faceStart = decoder.Int(ReadBlock(fs, texturePointer + 4, 4), 0);

            textureConfig = GetTextureConfigs(decoder, fs, texturePointer, textureCount, 0x10, true);
            faceCount = GetFaceCount();

            vertexBuffer = GetVertices(decoder, fs, head.vertexPointers[slotNum] + vertexIndex * 0x1C, head.UVpointers[slotNum] + vertexIndex * 0x08, vertexCount, 0x1C, 0x08);
            indexBuffer = GetIndices(decoder, fs, head.indexPointers[slotNum] + faceStart * 2, faceCount, vertexIndex);

            rgbas = ReadBlock(fs, head.rgbaPointers[slotNum] + vertexIndex * 4, vertexCount * 4);

            // OOOf hack
            byte[] vertBlock = ReadBlock(fs, head.vertexPointers[slotNum] + vertexIndex * 0x1C, vertexCount * 0x1C);
            for (int i = 0; i < vertBlock.Length / 0x1c; i++)
            {
                off_0C.Add(decoder.Uint(vertBlock, i * 0x1c + 0x18));
            }

        }

        public byte[] SerializeVerts()
        {
            int elemSize = 0x1C;
            byte[] outBytes = new byte[(vertexBuffer.Length / 8) * elemSize];

            for (int i = 0; i < vertexBuffer.Length / 8; i++)
            {
                int offset = i * elemSize;
                WriteFloat(outBytes, offset + 0x00, vertexBuffer[(i * 8) + 0]);
                WriteFloat(outBytes, offset + 0x04, vertexBuffer[(i * 8) + 1]);
                WriteFloat(outBytes, offset + 0x08, vertexBuffer[(i * 8) + 2]);
                WriteFloat(outBytes, offset + 0x0C, vertexBuffer[(i * 8) + 3]);
                WriteFloat(outBytes, offset + 0x10, vertexBuffer[(i * 8) + 4]);
                WriteFloat(outBytes, offset + 0x14, vertexBuffer[(i * 8) + 5]);
                WriteUint(outBytes, offset + 0x18, off_0C[i]);
            }

            return outBytes;
        }

    }
}
// Copyright (C) 2018-2026, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.IO;
using static LibReplanetizer.DataFunctions;
using static LibReplanetizer.Serializers.SerializerFunctions;

namespace LibReplanetizer.Models
{
    /// <summary>
    /// Structural header and relative entry-offset table for the engine 2D/billboard block.
    /// Pointed-to entry payloads are intentionally kept opaque.
    /// </summary>
    public class BillboardTable
    {
        const int HEADERSIZE = 0x10;

        public int baseValue { get; set; }
        public int unknown0x0C { get; set; }
        public List<int> entryOffsets { get; set; } = [];
        public byte[] dataBytes;

        public int entryCount => entryOffsets.Count;

        public BillboardTable(FileStream fs, int offset, int endOffset)
        {
            byte[] headerBytes = ReadBlock(fs, offset, HEADERSIZE);

            int entryCount = ReadInt(headerBytes, 0x00);
            baseValue = ReadInt(headerBytes, 0x04);
            int dataOffset = ReadInt(headerBytes, 0x08);
            unknown0x0C = ReadInt(headerBytes, 0x0C);

            int tableLength = entryCount * 0x04;
            byte[] entryTable = ReadBlock(fs, offset + HEADERSIZE, tableLength);

            for (int i = 0; i < entryCount; i++)
            {
                int entryOffset = ReadInt(entryTable, i * 0x04);
                entryOffsets.Add(entryOffset);
            }

            // We cannot know the length of the data sections, the game only stores
            // the offsets. So we use the next engine block's offset.
            dataBytes = ReadBlock(fs, offset + dataOffset, endOffset - (offset + dataOffset));
        }

        public int WriteBytes(FileStream fs)
        {
            int headerOffset = SeekReserve(fs, HEADERSIZE, 0x10);

            byte[] tableBytes = new byte[entryOffsets.Count * 0x04];
            for (int i = 0; i < entryOffsets.Count; i++)
                WriteInt(tableBytes, i * 0x04, entryOffsets[i]);

            SeekWrite(fs, tableBytes, 0x10);
            int dataOffset = SeekWrite(fs, dataBytes, 0x01);

            byte[] headerBytes = new byte[HEADERSIZE];

            WriteInt(headerBytes, 0x00, entryOffsets.Count);
            WriteInt(headerBytes, 0x04, baseValue);
            WriteInt(headerBytes, 0x08, GetRelativeOffset(dataOffset, headerOffset));
            WriteInt(headerBytes, 0x0C, unknown0x0C);

            WriteBytesAtOffset(fs, headerBytes, headerOffset);

            return headerOffset;
        }
    }
}

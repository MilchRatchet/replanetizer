// Copyright (C) 2018-2021, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using OpenTK.Mathematics;
using LibReplanetizer.LevelObjects;
using System;
using System.IO;
using Xunit;
using LibReplanetizer.Models.Animations;
using static LibReplanetizer.DataFunctions;

namespace LibReplanetizer.Tests.Animation
{
    public class FrameMarkerTests
    {
        private static Frame BuildFrame(byte scaleMarker, byte translationMarker)
        {
            byte[] frameBytes = new byte[0x20];
            WriteUshort(frameBytes, 0x06, 1);
            WriteUshort(frameBytes, 0x08, 0);
            WriteUshort(frameBytes, 0x0A, 1);
            WriteUshort(frameBytes, 0x0C, 8);
            WriteUshort(frameBytes, 0x0E, 1);

            WriteShort(frameBytes, 0x10, 4096);
            WriteShort(frameBytes, 0x12, 4096);
            WriteShort(frameBytes, 0x14, 4096);
            frameBytes[0x16] = 0;
            frameBytes[0x17] = scaleMarker;

            WriteShort(frameBytes, 0x18, 1024);
            WriteShort(frameBytes, 0x1A, 1024);
            WriteShort(frameBytes, 0x1C, 1024);
            frameBytes[0x1E] = 0;
            frameBytes[0x1F] = translationMarker;

            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, frameBytes);
                using FileStream stream = File.OpenRead(path);
                return new Frame(stream, GameType.RaC1, 0, 1);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static Moby.IngameMobyMemory BuildRuntimeMemory(byte marker)
        {
            const uint previousAnimationAddress = 0x1000;
            const uint currentAnimationAddress = 0x2000;

            byte[] mobyBytes = new byte[0x100];
            WriteInt(mobyBytes, 0x68, (int) previousAnimationAddress);
            WriteInt(mobyBytes, 0x6C, (int) currentAnimationAddress);

            byte[] animationBytes = new byte[0x30];
            WriteUshort(animationBytes, 0x06, 2);
            WriteUshort(animationBytes, 0x08, 8);
            WriteUshort(animationBytes, 0x0A, 1);
            WriteUshort(animationBytes, 0x0C, 16);
            WriteUshort(animationBytes, 0x0E, 1);
            WriteShort(animationBytes, 0x16, -32768);

            WriteShort(animationBytes, 0x18, 4096);
            WriteShort(animationBytes, 0x1A, 4096);
            WriteShort(animationBytes, 0x1C, 4096);
            animationBytes[0x1E] = 0;
            animationBytes[0x1F] = marker;

            WriteShort(animationBytes, 0x20, 1024);
            WriteShort(animationBytes, 0x22, 1024);
            WriteShort(animationBytes, 0x24, 1024);
            animationBytes[0x26] = 0;
            animationBytes[0x27] = marker;

            bool ReadMemory(uint address, byte[] destination)
            {
                uint baseAddress;
                if (address >= previousAnimationAddress && address < previousAnimationAddress + animationBytes.Length)
                {
                    baseAddress = previousAnimationAddress;
                }
                else if (address >= currentAnimationAddress && address < currentAnimationAddress + animationBytes.Length)
                {
                    baseAddress = currentAnimationAddress;
                }
                else
                {
                    return false;
                }

                int offset = checked((int) (address - baseAddress));
                if (offset + destination.Length > animationBytes.Length)
                {
                    return false;
                }

                Array.Copy(animationBytes, offset, destination, 0, destination.Length);
                return true;
            }

            var memory = new Moby.IngameMobyMemory();
            memory.LoadFromMemory(GameType.RaC1, mobyBytes, 0, ReadMemory);
            return memory;
        }

        [Theory]
        [InlineData(0x00, false)]
        [InlineData(0x80, true)]
        [InlineData(0x81, true)]
        [InlineData(0xFF, true)]
        public void AnyHighBitMarkerSelectsCurrentValueAcrossAnimationSources(byte marker, bool expected)
        {
            Frame frame = BuildFrame(marker, marker);
            Moby.IngameMobyMemory memory = BuildRuntimeMemory(marker);

            Assert.Equal(expected, frame.GetScalingUnk(0));
            Assert.Equal(expected, frame.GetTranslationUnk(0));
            Assert.Equal(expected, memory.previousAnimationData!.scalingUsesCurrentValue[0]);
            Assert.Equal(expected, memory.previousAnimationData.translationUsesCurrentValue[0]);
            Assert.Equal(expected, memory.currentAnimationData!.scalingUsesCurrentValue[0]);
            Assert.Equal(expected, memory.currentAnimationData.translationUsesCurrentValue[0]);
        }
    }

    public class AnimationManipulatorTests
    {
        [Fact]
        public void LoadFromMemory_PreservesFullManipulatorBoneOffset()
        {
            const uint manipulatorAddress = 0x3000;
            const uint boneOffset = 0x00010040;

            byte[] mobyBytes = new byte[0x100];
            WriteInt(mobyBytes, 0x64, (int) manipulatorAddress);

            byte[] manipulatorBytes = new byte[0x40];
            WriteInt(manipulatorBytes, 0x04, (int) boneOffset);

            bool ReadMemory(uint address, byte[] destination)
            {
                if (address != manipulatorAddress || destination.Length != manipulatorBytes.Length)
                {
                    return false;
                }

                Array.Copy(manipulatorBytes, destination, manipulatorBytes.Length);
                return true;
            }

            var memory = new Moby.IngameMobyMemory();
            memory.LoadFromMemory(GameType.RaC1, mobyBytes, 0, ReadMemory);

            Assert.Single(memory.manipulators);
            Assert.Equal(boneOffset, memory.manipulators[0].boneID);
        }
    }

    public class BoneDataTests
    {
        /// <summary>
        /// Builds a 0x10-byte RC1/2/3 BoneData block for bone at index <paramref name="num"/>.
        /// Translation values are pre-scaled by 1024 (raw format).
        /// </summary>
        private static byte[] BuildBoneDataBlock(int num, float txRaw, float tyRaw, float tzRaw, short unk, short parentRaw)
        {
            int totalSize = (num + 1) * 0x10;
            byte[] block = new byte[totalSize];
            int offset = num * 0x10;
            WriteFloat(block, offset + 0x00, txRaw);
            WriteFloat(block, offset + 0x04, tyRaw);
            WriteFloat(block, offset + 0x08, tzRaw);
            WriteShort(block, offset + 0x0C, unk);
            WriteShort(block, offset + 0x0E, parentRaw);
            return block;
        }

        [Fact]
        public void Constructor_RC1_ParsesTranslation()
        {
            // Translation is stored raw * 1024; constructor divides by 1024.
            byte[] block = BuildBoneDataBlock(0, 1024f, 2048f, 512f, 0x7000, 0x40);
            var bone = new BoneData(GameType.RaC1, block, 0);

            Assert.Equal(1.0f, bone.translation.X, 5);
            Assert.Equal(2.0f, bone.translation.Y, 5);
            Assert.Equal(0.5f, bone.translation.Z, 5);
        }

        [Fact]
        public void Constructor_RC1_ParsesParentIndex()
        {
            // parentRaw = index * 0x40; constructor divides by 0x40
            byte[] block = BuildBoneDataBlock(0, 0f, 0f, 0f, 0, (short) (3 * 0x40));
            var bone = new BoneData(GameType.RaC1, block, 0);

            Assert.Equal(3, bone.parent);
        }

        [Fact]
        public void Serialize_RC1_RoundTrip()
        {
            byte[] original = BuildBoneDataBlock(0, 1024f, 2048f, 512f, 0x7000, 0x40);
            var bone = new BoneData(GameType.RaC1, original, 0);
            byte[] serialized = bone.Serialize();

            Assert.Equal(original, serialized);
        }

        [Fact]
        public void Serialize_RC1_AtOffset_RoundTrip()
        {
            byte[] original = BuildBoneDataBlock(2, 3072f, -1024f, 0f, 0, (short) (1 * 0x40));
            var bone = new BoneData(GameType.RaC1, original, 2);
            byte[] serialized = bone.Serialize();

            // Serialized output is always 0x10 bytes (no num offset)
            Assert.Equal(0x10, serialized.Length);
            // Re-parse from serialized to verify values
            var bone2 = new BoneData(GameType.RaC1, serialized, 0);
            Assert.Equal(bone.translation.X, bone2.translation.X, 5);
            Assert.Equal(bone.translation.Y, bone2.translation.Y, 5);
            Assert.Equal(bone.translation.Z, bone2.translation.Z, 5);
            Assert.Equal(bone.parent, bone2.parent);
        }
    }

    public class BoneMatrixTests
    {
        private static byte[] BuildBoneMatrixBlock(int num, Matrix3x4 transform, Vector3 cumOffset, short unk3C, short parentRaw)
        {
            int totalSize = (num + 1) * 0x40;
            byte[] block = new byte[totalSize];
            int offset = num * 0x40;
            WriteMatrix3x4(block, offset, transform);
            WriteFloat(block, offset + 0x30, cumOffset.X * 1024.0f);
            WriteFloat(block, offset + 0x34, cumOffset.Y * 1024.0f);
            WriteFloat(block, offset + 0x38, cumOffset.Z * 1024.0f);
            WriteShort(block, offset + 0x3C, unk3C);
            WriteShort(block, offset + 0x3E, parentRaw);
            return block;
        }

        [Fact]
        public void Serialize_RC1_RoundTrip()
        {
            var transform = new Matrix3x4(
                1f, 0f, 0f, 0f,
                0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f);
            var cumOffset = new Vector3(1.0f, 2.0f, 3.0f);
            short unk = 0x7000;
            short parentRaw = (short) (2 * 0x40);

            byte[] original = BuildBoneMatrixBlock(0, transform, cumOffset, unk, parentRaw);
            var matrix = new BoneMatrix(GameType.RaC1, original, 0);
            byte[] serialized = matrix.Serialize();

            Assert.Equal(original, serialized);
        }
    }

    public class ModelSoundTests
    {
        private static byte[] BuildSoundBlock(int num,
            int off00, float distance, int masterVolume,
            int volume, int distortion, int distortion2,
            short off18, short listIndex, int off1C)
        {
            int totalSize = (num + 1) * 0x20;
            byte[] block = new byte[totalSize];
            int offset = num * 0x20;
            WriteInt(block, offset + 0x00, off00);
            WriteFloat(block, offset + 0x04, distance);
            WriteInt(block, offset + 0x08, masterVolume);
            WriteInt(block, offset + 0x0C, volume);
            WriteInt(block, offset + 0x10, distortion);
            WriteInt(block, offset + 0x14, distortion2);
            WriteShort(block, offset + 0x18, off18);
            WriteShort(block, offset + 0x1A, listIndex);
            WriteInt(block, offset + 0x1C, off1C);
            return block;
        }

        [Fact]
        public void Constructor_ParsesAllFieldsCorrectly()
        {
            byte[] block = BuildSoundBlock(0, 1, 10.0f, 100, 50, 5, 3, 7, 42, 0xABCD);
            var sound = new ModelSound(block, 0);

            Assert.Equal(1, sound.off00);
            Assert.Equal(10.0f, sound.distance);
            Assert.Equal(100, sound.masterVolume);
            Assert.Equal(50, sound.volume);
            Assert.Equal(5, sound.distortion);
            Assert.Equal(3, sound.distortion2);
            Assert.Equal((short) 7, sound.off18);
            Assert.Equal((short) 42, sound.listIndex);
            Assert.Equal(0xABCD, sound.off1C);
        }

        [Fact]
        public void Serialize_RoundTrip_MatchesOriginal()
        {
            byte[] original = BuildSoundBlock(0, 1, 10.0f, 100, 50, 5, 3, 7, 42, 0xABCD);
            var sound = new ModelSound(original, 0);
            byte[] serialized = sound.Serialize();

            Assert.Equal(original, serialized);
        }

        [Fact]
        public void Serialize_AtIndex_RoundTrip()
        {
            byte[] original = BuildSoundBlock(3, 9, -5.5f, 0, 255, -1, -2, -3, 0, 0);
            var sound = new ModelSound(original, 3);
            byte[] serialized = sound.Serialize();

            Assert.Equal(0x20, serialized.Length);
            var sound2 = new ModelSound(serialized, 0);
            Assert.Equal(sound.off00, sound2.off00);
            Assert.Equal(sound.distance, sound2.distance);
            Assert.Equal(sound.volume, sound2.volume);
        }
    }
}

// Copyright (C) 2018-2023, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.Threading;
using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using LibReplanetizer.Models;
using OpenTK.Mathematics;
using Replanetizer.Frames;
using Replanetizer.Utils;
using static LibReplanetizer.DataFunctions;

namespace Replanetizer.MemoryHook
{
    public class MemoryHookHandle : IDisposable
    {
        const long GUEST_MEMORY_HOST_BASE = 0x300000000;
        const int CAMERA_DATA_SIZE = 0x20;
        const int MOBY_TABLE_DATA_SIZE = 0x0C;
        const int MOBY_DATA_SIZE = 0x100;

        private readonly IProcessMemory? PROCESS_MEMORY;
        private readonly MemoryAddresses? ADDRESSES;

        private sealed class MemorySnapshot
        {
            public readonly object WRITE_LOCK = new object();
            public readonly Camera camera = new Camera();
            public readonly List<Moby.IngameMobyMemory> mobyMemory =
                new List<Moby.IngameMobyMemory>();
            public int mobyCount;
            public int frameNumber;
            public int readerCount;
        }

        private const int SNAPSHOT_COUNT = 3;
        private readonly MemorySnapshot[] SNAPSHOTS =
        {
            new MemorySnapshot(),
            new MemorySnapshot(),
            new MemorySnapshot()
        };
        private int PUBLISHED_SNAPSHOT_INDEX = -1;
        private int PREVIOUS_SNAPSHOT_INDEX = -1;
        private readonly byte[] CAMERA_DATA_BUFFER = new byte[CAMERA_DATA_SIZE];
        private readonly byte[] MOBY_TABLE_DATA_BUFFER = new byte[MOBY_TABLE_DATA_SIZE];
        private readonly byte[] FRAME_DATA_BUFFER = new byte[sizeof(int)];
        private byte[] MOBY_DATA_BUFFER = Array.Empty<byte>();
        private Thread? SNAPSHOT_THREAD;
        private volatile bool STOP_SNAPSHOT_THREAD;
        private readonly GameType GAME;

        public bool hookWorking { get; private set; } = false;
        private string errorMessage = "";

        public MemoryHookHandle(Level level)
        {
            GAME = level.game;
            switch (level.game.num)
            {
                case 1:
                    ADDRESSES = new MemoryAddresses
                    {
                        moby = 0x300A390A0,
                        camera = 0x300951500,
                        levelFrames = 0x300a10710
                    };
                    break;
                case 2:
                    ADDRESSES = new MemoryAddresses
                    {
                        moby = 0x3015927B0,
                        camera = 0x30146E3C0,
                        levelFrames = 0
                    };
                    break;
                case 3:
                    ADDRESSES = new MemoryAddresses
                    {
                        moby = 0x300F22260,
                        camera = 0x300D6B400,
                        levelFrames = 0
                    };
                    break;
                default:
                    hookWorking = false;
                    errorMessage = "Memory hooks are not supported for Deadlocked.";
                    return;
            }

            PROCESS_MEMORY = ProcessMemoryFactory.Create();
            if (PROCESS_MEMORY.IsAvailable)
            {
                hookWorking = true;
                errorMessage = PROCESS_MEMORY.ErrorMessage;
            }
            else
            {
                hookWorking = false;
                errorMessage = PROCESS_MEMORY.ErrorMessage;
                return;
            }

            level.EmplaceCommonData();
            SNAPSHOT_THREAD = new Thread(SnapshotLoop)
            {
                IsBackground = true,
                Name = "RPCS3 memory snapshot"
            };
            SNAPSHOT_THREAD.Start();
        }

        public string GetLastErrorMessage()
        {
            return errorMessage;
        }

        public void UpdateCamera(Camera camera)
        {
            if (!hookWorking) return;
            if (ADDRESSES == null) return;
            if (ADDRESSES.camera == 0) return;
            if (!TryAcquireCurrentSnapshot(out MemorySnapshot snapshot)) return;
            try
            {
                camera.position = snapshot.camera.position;
                camera.rotation = snapshot.camera.rotation;
            }
            finally
            {
                ReleaseSnapshot(snapshot);
            }
        }

        public void UpdateMobys(List<Moby> levelMobs, List<Model> models, LevelFrame frame, GameType game)
        {
            if (!hookWorking) return;
            if (ADDRESSES == null) return;
            if (ADDRESSES.moby == 0) return;
            if (!IsX64()) return;

            if (!TryAcquireCurrentSnapshot(out MemorySnapshot snapshot)) return;
            try
            {
                int numMobs = snapshot.mobyCount;
                while (levelMobs.Count < numMobs)
                {
                    Moby mob = new Moby(game);
                    levelMobs.Add(mob);
                    frame.levelRenderer?.Include(mob);
                }

                if (numMobs < levelMobs.Count)
                {
                    for (int i = numMobs; i < levelMobs.Count; i++)
                    {
                        levelMobs[i].SetDead();
                    }
                }

                for (int i = 0; i < numMobs; i++)
                {
                    levelMobs[i].ApplyMemory(snapshot.mobyMemory[i], models);
                }
            }
            finally
            {
                ReleaseSnapshot(snapshot);
            }
        }

        public int GetLevelFrameNumber()
        {
            if (!hookWorking) return -1;
            if (ADDRESSES == null) return -1;
            if (ADDRESSES.levelFrames == 0) return -1;

            if (!TryAcquireCurrentSnapshot(out MemorySnapshot snapshot)) return -1;
            try
            {
                return snapshot.frameNumber;
            }
            finally
            {
                ReleaseSnapshot(snapshot);
            }
        }

        private bool TryAcquireCurrentSnapshot(out MemorySnapshot snapshot)
        {
            while (true)
            {
                int index = Volatile.Read(ref PUBLISHED_SNAPSHOT_INDEX);
                if (index < 0)
                {
                    snapshot = null!;
                    return false;
                }

                MemorySnapshot candidate = SNAPSHOTS[index];
                Interlocked.Increment(ref candidate.readerCount);
                if (index == Volatile.Read(ref PUBLISHED_SNAPSHOT_INDEX))
                {
                    snapshot = candidate;
                    return true;
                }

                Interlocked.Decrement(ref candidate.readerCount);
            }
        }

        private static void ReleaseSnapshot(MemorySnapshot snapshot)
        {
            Interlocked.Decrement(ref snapshot.readerCount);
        }

        private void SnapshotLoop()
        {
            while (!STOP_SNAPSHOT_THREAD)
            {
                CaptureSnapshot();
                Thread.Sleep(1);
            }
        }

        private void CaptureSnapshot()
        {
            if (ADDRESSES == null) return;

            bool hasFrameCounter = ADDRESSES.levelFrames != 0;
            int frameBefore = -1;
            if (hasFrameCounter)
            {
                if (!ReadProcessInt(ADDRESSES.levelFrames, out frameBefore)) return;

                if (IsCurrentFrame(frameBefore)) return;
            }

            if (!TryGetWritableSnapshot(
                out int snapshotIndex,
                out MemorySnapshot snapshot))
            {
                return;
            }

            bool captureSucceeded;
            try
            {
                captureSucceeded = CaptureIntoSnapshot(snapshot, hasFrameCounter, frameBefore);
            }
            finally
            {
                Monitor.Exit(snapshot.WRITE_LOCK);
            }

            if (captureSucceeded)
            {
                int previousIndex = Volatile.Read(ref PUBLISHED_SNAPSHOT_INDEX);
                Volatile.Write(ref PREVIOUS_SNAPSHOT_INDEX, previousIndex);
                Volatile.Write(ref PUBLISHED_SNAPSHOT_INDEX, snapshotIndex);
            }
        }

        private bool IsCurrentFrame(int frameNumber)
        {
            if (!TryAcquireCurrentSnapshot(out MemorySnapshot snapshot)) return false;
            try
            {
                return snapshot.frameNumber == frameNumber;
            }
            finally
            {
                ReleaseSnapshot(snapshot);
            }
        }

        private bool TryGetWritableSnapshot(
            out int snapshotIndex,
            out MemorySnapshot snapshot)
        {
            int publishedIndex = Volatile.Read(ref PUBLISHED_SNAPSHOT_INDEX);
            for (int i = 0; i < SNAPSHOT_COUNT; i++)
            {
                if (i == publishedIndex) continue;

                MemorySnapshot candidate = SNAPSHOTS[i];
                if (Volatile.Read(ref candidate.readerCount) != 0) continue;

                Monitor.Enter(candidate.WRITE_LOCK);
                if (i != Volatile.Read(ref PUBLISHED_SNAPSHOT_INDEX) &&
                    Volatile.Read(ref candidate.readerCount) == 0)
                {
                    snapshotIndex = i;
                    snapshot = candidate;
                    return true;
                }

                Monitor.Exit(candidate.WRITE_LOCK);
            }

            snapshotIndex = -1;
            snapshot = null!;
            return false;
        }

        private bool CaptureIntoSnapshot(
            MemorySnapshot snapshot,
            bool hasFrameCounter,
            int frameBefore)
        {
            if (ADDRESSES == null) return false;

            bool processSuspended = false;
            if (hasFrameCounter)
            {
                if (PROCESS_MEMORY == null || !PROCESS_MEMORY.Suspend()) return false;
                processSuspended = true;
            }

            try
            {
                if (!ReadProcessBytes(ADDRESSES.camera, CAMERA_DATA_BUFFER)) return false;
                if (!ReadProcessBytes(ADDRESSES.moby, MOBY_TABLE_DATA_BUFFER)) return false;

                uint firstMoby = ReadUint(MOBY_TABLE_DATA_BUFFER, 0x00);
                uint lastMoby = ReadUint(MOBY_TABLE_DATA_BUFFER, 0x08);
                if (!TryGetMobyRange(firstMoby, lastMoby, out int mobyCount, out int mobyDataSize))
                {
                    return false;
                }

                if (MOBY_DATA_BUFFER.Length != mobyDataSize)
                {
                    MOBY_DATA_BUFFER = new byte[mobyDataSize];
                }

                long mobyAddress = GUEST_MEMORY_HOST_BASE + firstMoby;
                if (!ReadProcessBytes(mobyAddress, MOBY_DATA_BUFFER)) return false;

                snapshot.camera.position = new Vector3(
                    ReadFloat(CAMERA_DATA_BUFFER, 0x00),
                    ReadFloat(CAMERA_DATA_BUFFER, 0x04),
                    ReadFloat(CAMERA_DATA_BUFFER, 0x08));
                snapshot.camera.rotation = new Vector3(
                    -ReadFloat(CAMERA_DATA_BUFFER, 0x14),
                    ReadFloat(CAMERA_DATA_BUFFER, 0x10),
                    ReadFloat(CAMERA_DATA_BUFFER, 0x18) - (float) (Math.PI / 2));

                while (snapshot.mobyMemory.Count < mobyCount)
                {
                    snapshot.mobyMemory.Add(new Moby.IngameMobyMemory());
                }

                for (int i = 0; i < mobyCount; i++)
                {
                    snapshot.mobyMemory[i].LoadFromMemory(
                        GAME,
                        MOBY_DATA_BUFFER,
                        i * MOBY_DATA_SIZE,
                        ReadGuestProcessBytes);
                }

                int frameAfter = frameBefore;
                if (hasFrameCounter && !ReadProcessInt(ADDRESSES.levelFrames, out frameAfter))
                {
                    return false;
                }

                snapshot.mobyCount = mobyCount;
                snapshot.frameNumber = frameAfter;
                return true;
            }
            finally
            {
                if (processSuspended)
                {
                    PROCESS_MEMORY?.Resume();
                }
            }
        }

        private bool ReadProcessBytes(long address, byte[] buffer)
        {
            return PROCESS_MEMORY?.Read(address, buffer) ?? false;
        }

        private bool ReadGuestProcessBytes(uint address, byte[] buffer)
        {
            return ReadProcessBytes(GUEST_MEMORY_HOST_BASE + address, buffer);
        }

        private bool ReadProcessInt(long address, out int value)
        {
            bool readSucceeded = ReadProcessBytes(address, FRAME_DATA_BUFFER);
            value = readSucceeded ? ReadInt(FRAME_DATA_BUFFER, 0) : -1;
            return readSucceeded;
        }

        private bool TryGetMobyRange(uint firstMoby, uint lastMoby, out int mobyCount, out int mobyDataSize)
        {
            mobyCount = 0;
            mobyDataSize = 0;
            if (lastMoby < firstMoby) return false;

            ulong mobySpan = (ulong) lastMoby - firstMoby;
            if (mobySpan % MOBY_DATA_SIZE != 0) return false;

            ulong mobyCountValue = mobySpan / MOBY_DATA_SIZE + 1;
            ulong mobyDataSizeValue = mobyCountValue * MOBY_DATA_SIZE;
            if (mobyCountValue > int.MaxValue || mobyDataSizeValue > int.MaxValue) return false;

            mobyCount = (int) mobyCountValue;
            mobyDataSize = (int) mobyDataSizeValue;
            return true;
        }

        private bool IsX64()
        {
            // The memory hook functions depend on reading 64 bit addresses,
            // thus we need to check that the pointer size is 8 (ie 64 bits)
            return IntPtr.Size == 8;
        }

        public void HandleSplineTranslation(Level level, Spline spline, int currentSplineVertex)
        {
            /*
             * This code was already commented out before I moved it here.
             * TODO: Uncomment and test this at some point. Contributions welcomed.
             *
            //write at 0x346BA1180 + 0xC0 + spline.offset + currentSplineVertex * 0x10;
            // List of splines 0x300A51BE0

            byte[] ptrBuff = new byte[0x04];
            int bytesRead = 0;
            ReadProcessMemory(processHandle, 0x300A51BE0 + level.splines.IndexOf(spline) * 0x04, ptrBuff, ptrBuff.Length, ref bytesRead);
            long splinePtr = ReadUint(ptrBuff, 0) + 0x300000010;

            byte[] buff = new byte[0x0C];
            Vector3 vec = spline.GetVertex(currentSplineVertex);
            WriteFloat(buff, 0x00, vec.X);
            WriteFloat(buff, 0x04, vec.Y);
            WriteFloat(buff, 0x08, vec.Z);

            WriteProcessMemory(processHandle, splinePtr + currentSplineVertex * 0x10, buff, buff.Length, ref bytesRead);
            */
        }

        public void Dispose()
        {
            STOP_SNAPSHOT_THREAD = true;
            SNAPSHOT_THREAD?.Join();
            PROCESS_MEMORY?.Dispose();
        }
    }
}

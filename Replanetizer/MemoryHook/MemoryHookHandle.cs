// Copyright (C) 2018-2023, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
        // Read and write acceess
        const int PROCESS_WM_READ = 0x38;
#if _WINDOWS
        const int PROCESS_SUSPEND_RESUME = 0x0800;
        const long GUEST_MEMORY_HOST_BASE = 0x300000000;
        const int CAMERA_DATA_SIZE = 0x20;
        const int MOBY_TABLE_DATA_SIZE = 0x0C;
        const int MOBY_DATA_SIZE = 0x100;
#endif

#if _WINDOWS
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, Int64 lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, Int64 lpBaseAddress, byte[] lpBuffer, int nSize, ref int lpNumberOfBytesWritten);

        [DllImport("ntdll.dll")]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll")]
        private static extern int NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
#endif

        private readonly Process? PROCESS;
        private readonly IntPtr PROCESS_HANDLE;
        private readonly MemoryAddresses? ADDRESSES;

#if _WINDOWS
        private sealed class MemorySnapshot
        {
            public readonly byte[] cameraData;
            public readonly byte[] mobyTableData;
            public readonly byte[] mobyData;
            public readonly int mobyCount;
            public readonly int frameNumber;

            public MemorySnapshot(
                byte[] cameraData,
                byte[] mobyTableData,
                byte[] mobyData,
                int mobyCount,
                int frameNumber)
            {
                this.cameraData = cameraData;
                this.mobyTableData = mobyTableData;
                this.mobyData = mobyData;
                this.mobyCount = mobyCount;
                this.frameNumber = frameNumber;
            }
        }

        private readonly object SNAPSHOT_LOCK = new object();
        private MemorySnapshot? SNAPSHOT;
        private Thread? SNAPSHOT_THREAD;
        private volatile bool STOP_SNAPSHOT_THREAD;
#endif

        public bool hookWorking { get; private set; } = false;
        private string errorMessage = "";

        public MemoryHookHandle(Level level)
        {
#if _WINDOWS
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

            Process[] processList = Process.GetProcessesByName("rpcs3");
            if (processList.Length > 0)
            {
                PROCESS = processList[0];
                PROCESS_HANDLE = OpenProcess(PROCESS_WM_READ | PROCESS_SUSPEND_RESUME, false, PROCESS.Id);

                hookWorking = true;
                errorMessage = "Success!";
            }
            else
            {
                hookWorking = false;
                errorMessage = "Failed to find a running RPCS3 process.";
            }

            if (hookWorking)
            {
                level.EmplaceCommonData();
                SNAPSHOT_THREAD = new Thread(SnapshotLoop)
                {
                    IsBackground = true,
                    Name = "RPCS3 memory snapshot"
                };
                SNAPSHOT_THREAD.Start();
            }
#else
            hookWorking = false;
            errorMessage = "Memory hooks are only supported for Windows.";
#endif
        }

        public string GetLastErrorMessage()
        {
            return errorMessage;
        }

        public void UpdateCamera(Camera camera)
        {
#if _WINDOWS
            if (!hookWorking) return;
            if (ADDRESSES == null) return;
            if (ADDRESSES.camera == 0) return;
            lock (SNAPSHOT_LOCK)
            {
                if (SNAPSHOT == null) return;

                camera.position = new Vector3(
                    ReadFloat(SNAPSHOT.cameraData, 0x00),
                    ReadFloat(SNAPSHOT.cameraData, 0x04),
                    ReadFloat(SNAPSHOT.cameraData, 0x08));
                camera.rotation = new Vector3(
                    -ReadFloat(SNAPSHOT.cameraData, 0x14),
                    ReadFloat(SNAPSHOT.cameraData, 0x10),
                    ReadFloat(SNAPSHOT.cameraData, 0x18) - (float) (Math.PI / 2));
            }
#endif
        }

        public void UpdateMobys(List<Moby> levelMobs, List<Model> models, LevelFrame frame, GameType game)
        {
#if _WINDOWS
            if (!hookWorking) return;
            if (ADDRESSES == null) return;
            if (ADDRESSES.moby == 0) return;
            if (!IsX64()) return;

            lock (SNAPSHOT_LOCK)
            {
                if (SNAPSHOT == null) return;

                int numMobs = SNAPSHOT.mobyCount;
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
                    levelMobs[i].UpdateFromMemory(SNAPSHOT.mobyData, i * MOBY_DATA_SIZE, models);
                }
            }
#endif
        }

        public int GetLevelFrameNumber()
        {
#if _WINDOWS
            if (!hookWorking) return -1;
            if (ADDRESSES == null) return -1;
            if (ADDRESSES.levelFrames == 0) return -1;

            lock (SNAPSHOT_LOCK)
            {
                return SNAPSHOT?.frameNumber ?? -1;
            }
#else
            return -1;
#endif
        }

#if _WINDOWS
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

                lock (SNAPSHOT_LOCK)
                {
                    if (SNAPSHOT?.frameNumber == frameBefore) return;
                }
            }

            bool processSuspended = false;
            if (hasFrameCounter)
            {
                if (NtSuspendProcess(PROCESS_HANDLE) < 0) return;
                processSuspended = true;
            }

            try
            {
                byte[] cameraData = new byte[CAMERA_DATA_SIZE];
                byte[] mobyTableData = new byte[MOBY_TABLE_DATA_SIZE];
                if (!ReadProcessBytes(ADDRESSES.camera, cameraData)) return;
                if (!ReadProcessBytes(ADDRESSES.moby, mobyTableData)) return;

                uint firstMoby = ReadUint(mobyTableData, 0x00);
                uint lastMoby = ReadUint(mobyTableData, 0x08);
                if (!TryGetMobyRange(firstMoby, lastMoby, out int mobyCount, out int mobyDataSize)) return;

                byte[] mobyData = new byte[mobyDataSize];
                long mobyAddress = GUEST_MEMORY_HOST_BASE + firstMoby;
                if (!ReadProcessBytes(mobyAddress, mobyData)) return;

                int frameAfter = frameBefore;
                if (hasFrameCounter && !ReadProcessInt(ADDRESSES.levelFrames, out frameAfter)) return;

                MemorySnapshot snapshot = new MemorySnapshot(
                    cameraData,
                    mobyTableData,
                    mobyData,
                    mobyCount,
                    frameAfter);
                lock (SNAPSHOT_LOCK)
                {
                    SNAPSHOT = snapshot;
                }
                return;
            }
            finally
            {
                if (processSuspended)
                {
                    NtResumeProcess(PROCESS_HANDLE);
                }
            }
        }

        private bool ReadProcessBytes(long address, byte[] buffer)
        {
            int bytesRead = 0;
            bool readSucceeded = ReadProcessMemory(PROCESS_HANDLE, address, buffer, buffer.Length, ref bytesRead);
            return readSucceeded && bytesRead == buffer.Length;
        }

        private bool ReadProcessInt(long address, out int value)
        {
            byte[] buffer = new byte[sizeof(int)];
            int bytesRead = 0;
            bool readSucceeded = ReadProcessMemory(PROCESS_HANDLE, address, buffer, buffer.Length, ref bytesRead);
            value = readSucceeded && bytesRead == buffer.Length ? ReadInt(buffer, 0) : -1;
            return readSucceeded && bytesRead == buffer.Length;
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
#endif

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
#if _WINDOWS
            STOP_SNAPSHOT_THREAD = true;
            SNAPSHOT_THREAD?.Join();
            if (PROCESS_HANDLE != IntPtr.Zero)
            {
                CloseHandle(PROCESS_HANDLE);
            }
#endif
            PROCESS?.Dispose();
        }
    }
}

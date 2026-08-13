// Copyright (C) 2018-2026, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Diagnostics;
using static LibReplanetizer.DataFunctions;

namespace LibReplanetizer.LevelObjects
{
    public abstract class PVars
    {
        protected readonly byte[] bytes;

        protected PVars(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            this.bytes = (byte[]) bytes.Clone();
        }

        public int Length => bytes.Length;

        public ReadOnlyMemory<byte> RawBytes => bytes;

        public virtual byte[] ToByteArray()
        {
            return (byte[]) bytes.Clone();
        }
    }

    public sealed class RawPVars : PVars
    {
        public RawPVars() : this([])
        {
        }

        public RawPVars(byte[] bytes) : base(bytes)
        {
        }
    }

    // Example layout only; replace the offsets and model registration with reverse-engineered data.
    public sealed class ExamplePVars : PVars
    {
        public const int GameNumber = 1;
        public const int ModelID = 0xFFFF;

        public int ExampleValue { get { return ReadInt(bytes, 0x00); } }
        public short ExampleFlags { get { return ReadShort(bytes, 0x04); } }
        public float ExampleTimer { get { return ReadFloat(bytes, 0x08); } }

        public ExamplePVars(byte[] bytes) : base(bytes)
        {
            Debug.Assert(bytes.Length == 0x0C);
        }
    }

    public static class PVarFactory
    {
        public static PVars Create(GameType game, int modelID, PVars rawPVars)
        {
            return (game.num, modelID) switch
            {
                (ExamplePVars.GameNumber, ExamplePVars.ModelID) => new ExamplePVars(rawPVars.ToByteArray()),
                _ => rawPVars
            };
        }
    }
}

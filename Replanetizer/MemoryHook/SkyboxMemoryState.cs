using System;
using System.Collections.Generic;

namespace Replanetizer.MemoryHook
{
    public enum SkyboxMemorySource
    {
        Unavailable,
        Identity,
        FrameCounter,
        LayerRotations
    }

    public sealed class SkyboxMemoryState
    {
        public const int MAX_SKYBOX_LAYERS = 64;
        public float[] layerRotations = new float[MAX_SKYBOX_LAYERS];
        public int planetId = -1;
        public int frameValue;
        public int layerCount;
        public SkyboxMemorySource source = SkyboxMemorySource.Unavailable;
        public bool available;

        public void Reset()
        {
            Array.Clear(layerRotations, 0, layerRotations.Length);
            planetId = -1;
            frameValue = 0;
            layerCount = 0;
            source = SkyboxMemorySource.Unavailable;
            available = false;
        }

        public void CopyFrom(SkyboxMemoryState other)
        {
            other.layerRotations.CopyTo(layerRotations, 0);
            planetId = other.planetId;
            frameValue = other.frameValue;
            layerCount = other.layerCount;
            source = other.source;
            available = other.available;
        }
    }
}

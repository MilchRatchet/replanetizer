using System.Collections.Generic;

namespace Replanetizer.MemoryHook
{
    public sealed class SkyboxMemoryState
    {
        public const int MAX_SKYBOX_LAYERS = 64;
        public float[] layerRotations = new float[MAX_SKYBOX_LAYERS];

        public void CopyFrom(SkyboxMemoryState other)
        {
            other.layerRotations.CopyTo(layerRotations, 0);
        }
    }
}

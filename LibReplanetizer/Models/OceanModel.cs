using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LibReplanetizer.Models
{
    public class OceanModel : Model
    {
        public OceanModel(FileStream fs)
        {
            vertexBuffer = GetVerticesOcean(fs, 0, 15497, 0x14);
            indexBuffer = GetIndices(fs, 15497 * 0x14, 3 * 30738);
        }
    }
}

using LibReplanetizer.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace LibReplanetizer.Parsers
{
    public class OceanVertsParser : RatchetFileParser, IDisposable
    {

        GameType game;

        public OceanVertsParser(GameType game, string oceanFile) : base(oceanFile)
        {
            this.game = game;
        }

        public OceanModel GetModel()
        {
            return new OceanModel(fileStream);
        }

        public void Dispose()
        {
            fileStream.Close();
        }
    }
}

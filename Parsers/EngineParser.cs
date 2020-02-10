﻿using System;
using System.Collections.Generic;
using RatchetEdit.LevelObjects;
using RatchetEdit.Models;
using RatchetEdit.Models.Animations;
using RatchetEdit.Headers;

namespace RatchetEdit.Parsers
{
    public class EngineParser : RatchetFileParser, IDisposable
    {
        public EngineHeader engineHead;

        public EngineParser(Decoder decoder, string engineFile) : base(decoder, engineFile)
        {
            engineHead = new EngineHeader(decoder, fileStream);
        }

        public List<Model> GetMobyModels(Decoder decoder)
        {
            return GetMobyModels(decoder, engineHead.mobyModelPointer);
        }

        public List<Model> GetTieModels()
        {
            return GetTieModels(engineHead.tieModelPointer, engineHead.tieModelCount);
        }

        public List<Model> GetShrubModels()
        {
            return GetShrubModels(engineHead.shrubModelPointer, engineHead.shrubModelCount);
        }

        public List<Texture> GetTextures()
        {
            return GetTextures(engineHead.texturePointer, engineHead.textureCount);
        }

        public List<Tie> GetTies(List<Model> tieModels)
        {
            return GetTies(tieModels, engineHead.tiePointer, engineHead.tieCount);
        }

        public List<Light> GetLights()
        {
            return GetLights(engineHead.lightPointer, engineHead.lightCount);
        }

        public List<Shrub> GetShrubs(List<Model> shrubModels)
        {
            return GetShrubs(shrubModels, engineHead.shrubPointer, engineHead.shrubCount);
        }

        public List<TerrainFragment> GetTerrainModels()
        {
            return GetTerrainModels(engineHead.terrainPointer);
        }

        public SkyboxModel GetSkyboxModel(Decoder decoder)
        {
            return GetSkyboxModel(decoder, engineHead.skyboxPointer);
        }

        public List<UiElement> GetUiElements()
        {
            return GetUiElements(engineHead.uiElementPointer);
        }

        public List<Animation> GetPlayerAnimations( MobyModel ratchet)
        {
            return GetPlayerAnimations(engineHead.playerAnimationPointer, ratchet);
        }

        public List<Model> GetWeapons(Decoder decoder)
        {
            return GetWeapons(decoder, engineHead.weaponPointer, engineHead.weaponCount);
        }

        public byte[] GetLightConfig()
        {
            return GetLightConfig(engineHead.lightConfigPointer);
        }

        public List<int> GetTextureConfigMenu()
        {
            return GetTextureConfigMenu(engineHead.textureConfigMenuPointer, engineHead.textureConfigMenuCount);
        }

        public Model GetCollisionModel()
        {
            return GetCollisionModel(engineHead.collisionPointer);
        }


        // TODO: Arbitrary bytes, parse properly
        public byte[] GetTerrainBytes()
        {
            if(engineHead.renderDefPointer > 0)
            {
                return GetTerrainBytes(engineHead.terrainPointer, engineHead.renderDefPointer - engineHead.terrainPointer);
            }
            else
            {
                return GetTerrainBytes(engineHead.terrainPointer, engineHead.skyboxPointer - engineHead.terrainPointer);
            }
            
        }

        public byte[] GetRenderDefBytes()
        {
            if(engineHead.renderDefPointer > 0)
            {
                return ReadArbBytes(engineHead.renderDefPointer, engineHead.collisionPointer - engineHead.renderDefPointer);
            }
            else
            {
                return new byte[0];
            }
        }

        public byte[] GetCollisionBytes()
        {
            if(engineHead.collisionPointer > 0)
            {
                return ReadArbBytes(engineHead.collisionPointer, engineHead.mobyModelPointer - engineHead.collisionPointer);
            }
            else
            {
                return new byte[0];
            }
        }

        public byte[] GetBillboardBytes()
        {
            return ReadArbBytes(engineHead.texture2dPointer, engineHead.soundConfigPointer - engineHead.texture2dPointer);
        }

        public byte[] GetSoundConfigBytes()
        {
            return ReadArbBytes(engineHead.soundConfigPointer, engineHead.lightPointer - engineHead.soundConfigPointer);
        }

        public GameType DetectGame()
        {
            return DetectGame(0xA0);
        }

        public void Dispose()
        {
            fileStream.Close();
        }
    }
}

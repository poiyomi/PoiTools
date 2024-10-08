using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Font = UnityEngine.Font;
using Color = UnityEngine.Color;

namespace Poi.Tools.Extra
{
    [PoiExternalTool(PoiExternalToolRegistry.PoiFontToolId)]
    public class MSDFFontConverter : IPoiExternalTool
    {
        static readonly string BasePathLocal = "Packages/com.poiyomi.tools/MSDF Fonts";
        static readonly string TempPathLocal = $"{BasePathLocal}/Temp";
        static readonly string TempGlyphPathLocal = $"{TempPathLocal}/tempGlyph.png";
        static readonly string TempPathFull = Path.GetFullPath($"{BasePathLocal}/Temp");
        static readonly string TempGlyphPathFull = $"{Path.GetFullPath(TempGlyphPathLocal)}";
        static readonly string MSDFGenPathFull = Path.GetFullPath($"{BasePathLocal}/Editor/bin/msdfgen.exe");

        static readonly int TileSize = 32;
        static readonly int DistanceRange = 4;
        static readonly int CharacterSpacing = 4;
        static readonly int CharacterPadding = 0;

        static bool TryGenerateFontTilesetTexture(Font font, out Texture2D generatedTexture)
        {
            generatedTexture = null;

            try
            {
                string fontAssetPath = AssetDatabase.GetAssetPath(font);

                TrueTypeFontImporter fontImporter = AssetImporter.GetAtPath(fontAssetPath) as TrueTypeFontImporter;

                if(fontImporter == null)
                {
                    Debug.LogError("Could not import mesh asset! Builtin Unity fonts like Arial don't work unless you put them in the project directory!");
                    return false;
                }

                fontImporter.characterSpacing = 4;
                fontImporter.characterPadding = 2;

                fontImporter.SaveAndReimport();

                int tileMapSize = TileSize * 16;
                generatedTexture = new Texture2D(tileMapSize, tileMapSize, TextureFormat.ARGB32, false, true);

                for(int x = 0; x < generatedTexture.width; ++x)
                {
                    for(int y = 0; y < generatedTexture.height; ++y)
                    {
                        generatedTexture.SetPixel(x, y, Color.black);
                    }
                }

                string fontPathFull = Path.GetFullPath(fontAssetPath);
                for(int tileIndex = 1; tileIndex < 256; tileIndex++)
                {
                    char c = (char)tileIndex;
                    if(!font.HasCharacter(c))
                        continue;

                    EditorUtility.DisplayProgressBar("Converting Font", $"Glyph {tileIndex + 1}/256", tileIndex / 255.0f);

                    var currentGlyphTex = GenerateGlyphTexture(fontPathFull, tileIndex);
                    if(!currentGlyphTex)
                        continue;

                    int rowoffset = TileSize * (tileIndex / 16) + CharacterPadding / 2;
                    int columnoffset = TileSize * (tileIndex % 16) + CharacterPadding / 2;
                    for(int y = 0; y < currentGlyphTex.height; ++y)
                    {
                        for(int x = 0; x < currentGlyphTex.width; ++x)
                        {
                            Color glyphCol = currentGlyphTex.GetPixel(x, currentGlyphTex.height - 1 - y);
                            generatedTexture.SetPixel(columnoffset + x, tileMapSize - 1 - (rowoffset + y), glyphCol);
                        }
                    }
                }

                generatedTexture.Apply(false);
            }
            catch(Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return true;
        }

        static Texture2D GenerateGlyphTexture(string fontPathFull, int UTFChar)
        {
            System.Diagnostics.Process msdfProcess = new System.Diagnostics.Process()
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.GetFullPath(MSDFGenPathFull),
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true,
            };

            if(!Directory.Exists(TempPathFull))
                Directory.CreateDirectory(TempPathFull);

            int tileSize = TileSize - CharacterPadding;
            string argStr = $"mtsdf -o \"{TempGlyphPathFull}\" -font \"{fontPathFull}\" {UTFChar} -size {tileSize} {tileSize} -pxrange {DistanceRange} -autoframe";

            msdfProcess.StartInfo.Arguments = argStr;
            msdfProcess.Start();
            msdfProcess.WaitForExit();

            if(!File.Exists(TempGlyphPathLocal))
            {
                Debug.LogWarning($"Could not load glyph {UTFChar}");
                return null;
            }

            Texture2D loadedGlyph = new Texture2D(tileSize, tileSize);
            ImageConversion.LoadImage(loadedGlyph, File.ReadAllBytes(TempGlyphPathLocal), false);

            return loadedGlyph;
        }

        public void Execute(UnityEngine.Object obj)
        {
            Font font = (Font)obj;
            string fontAssetPath = AssetDatabase.GetAssetPath(font);
            TrueTypeFontImporter fontImporter = AssetImporter.GetAtPath(fontAssetPath) as TrueTypeFontImporter;

            if(fontImporter == null)
                Debug.LogError("Could not import mesh asset! Builtin Unity fonts like Arial don't work unless you put them in the project directory!");

            fontImporter.characterSpacing = CharacterSpacing;
            fontImporter.characterPadding = CharacterPadding;

            fontImporter.SaveAndReimport();

            if(!TryGenerateFontTilesetTexture(font, out Texture2D newAtlas))
            {
                Debug.LogError($"Failed to convert font {font.name}");
                return;
            }

            string atlasName = Path.GetFileNameWithoutExtension(fontAssetPath) + "_atlas.asset";
            string atlasPath = Path.Combine(Path.GetDirectoryName(fontAssetPath), atlasName);

            AssetDatabase.CreateAsset(newAtlas, atlasPath);
            EditorGUIUtility.PingObject(newAtlas);
        }
    }
}
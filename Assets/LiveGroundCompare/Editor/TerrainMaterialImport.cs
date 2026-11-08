using UnityEditor;

namespace UOClient.LiveGroundCompare.Editor
{
    /// <summary>
    /// Import rules for the natural terrain's materials (Textures/Resources/TerrainMaterials, made by
    /// E:/UOOverhaul/tools/terrain_materials_prep.py): every albedo the same size and format (sRGB, height in alpha) and
    /// every normal map the same, so the runtime can stack them into Texture2DArrays with Graphics.CopyTexture
    /// (TerrainMaterialSet). Repeat wrap, mipmaps, trilinear.
    /// </summary>
    internal sealed class TerrainMaterialImport : AssetPostprocessor
    {
        private const int Size = 1024;

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/TerrainMaterials/"))
                return;
            bool normal = path.EndsWith("_normal.png");
            if (!normal && !path.EndsWith("_albedo.png"))
                return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.alphaSource = normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = Size;
            importer.isReadable = false;
            var standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = Size;
            standalone.format = normal ? TextureImporterFormat.BC5 : TextureImporterFormat.DXT5;
            importer.SetPlatformTextureSettings(standalone);
            var fallback = importer.GetDefaultPlatformTextureSettings();
            fallback.maxTextureSize = Size;
            fallback.format = normal ? TextureImporterFormat.BC5 : TextureImporterFormat.DXT5;
            importer.SetPlatformTextureSettings(fallback);
        }
    }
}

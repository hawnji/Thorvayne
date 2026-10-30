using UnityEditor;
using UnityEngine;

// Item icons (Resources/Icons, from Extras/make_icons.py) are drawn as-is by the game UI: keep them sharp,
// with clean transparent edges and no mipmaps or resizing.
public class ThorvayneIconImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("/Resources/Icons/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
    }
}

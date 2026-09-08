#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>Keep the game's approved full-bleed artwork in every Android launcher slot.</summary>
public static class Level0LauncherIconSetup
{
    public const string ArtworkPath = "Assets/Level0VR/Branding/Level0Icon.png";
    public const string AdaptiveForegroundPath = "Assets/Level0VR/Branding/Level0AdaptiveForeground.png";

    [MenuItem("Tools/Level0/Apply Launcher Icon")]
    public static void Apply()
    {
        Texture2D artwork = Load(ArtworkPath);
        Texture2D emptyForeground = Load(AdaptiveForegroundPath);
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { artwork });
        int slotCount = 0;
        foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
        {
            PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (PlatformIcon icon in icons)
            {
                // Empty slots report layerCount=0. The supported minimum defines their layout.
                if (icon.minLayerCount > 1)
                    icon.SetTextures(new[] { artwork, emptyForeground });
                else
                    icon.SetTexture(artwork, 0);
                slotCount++;
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("LEVEL0_LAUNCHER_ICON_READY slots=" + slotCount + " source=" + ArtworkPath +
            " adaptive=full-bleed-artwork-with-transparent-foreground");
    }

    private static Texture2D Load(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) throw new InvalidOperationException("Missing Level0 icon: " + path);
        if (importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (!texture) throw new InvalidOperationException("Could not load Level0 icon: " + path);
        return texture;
    }
}
#endif

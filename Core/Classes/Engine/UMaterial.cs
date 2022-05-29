﻿using Core.Classes.Core;
using Core.Types;
using Core.Types.PackageTables;

namespace Core.Classes.Engine;

[NativeOnlyClass("Engine", "Material", typeof(UMaterialInterface))]
public class UMaterial : UMaterialInterface
{
    public UMaterial(FName name, UClass? @class, UObject? outer, UnrealPackage ownerPackage, UObject? objectArchetype = null) : base(name, @class, outer,
        ownerPackage, objectArchetype)
    {
    }

    public FMaterialResource[] FMaterialResources { get; set; } = new FMaterialResource[2];
}

public class FMaterial
{
    public List<string> CompileErrors { get; set; } = new();
    public Dictionary<ObjectIndex, int> TextureDependencyLengthMap { get; set; } = new();

    public int MaxTextureDependencyLength { get; set; }

    public FGuid ID { get; set; }

    public uint NumUserTexCoords { get; set; }

    public List<ObjectIndex> UniformExpressionTextures { get; set; }

    public bool bUsesSceneColorTemp { get; set; }
    public bool bUsesSceneDepthTemp { get; set; }
    public bool bUsesDynamicParameterTemp { get; set; }
    public bool bUsesLightmapUVsTemp { get; set; }
    public bool bUsesMaterialVertexPositionOffsetTemp { get; set; }
    public bool UsingTransforms { get; set; }
    public List<FTextureLookupInfo> FTextureLookupInfos { get; set; } = new();
    public bool DummyDroppedFallbackComponents { get; set; }

    // 16 unknown bytes at the end 
    public byte[]? UnknownBytes { get; set; }
}

public class FMaterialResource : FMaterial
{
}

public class FTextureLookupInfo
{
    public int TexCoordIndex { get; set; }
    public int TextureIndex { get; set; }
    public float UScale { get; set; }
    public float VScale { get; set; }
}
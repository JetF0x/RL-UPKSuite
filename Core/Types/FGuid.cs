﻿namespace Core.Types;

public class FGuid
{
    public uint A { get; private set; }
    public uint B { get; private set; }
    public uint C { get; private set; }
    public uint D { get; private set; }

    public void Deserialize(BinaryReader reader)
    {
        A = reader.ReadUInt32();
        B = reader.ReadUInt32();
        C = reader.ReadUInt32();
        D = reader.ReadUInt32();
    }
}
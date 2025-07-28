using Unity.Entities;
using Unity.NetCode;

[GhostComponent(PrefabType = GhostPrefabType.AllPredicted)]
public struct AIShipCommandData : IComponentData
{
    public byte left;

    public byte right;

    public byte thrust;

    public byte shoot;
}
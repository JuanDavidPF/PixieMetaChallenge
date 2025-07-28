using Unity.Entities;
using UnityEngine;

public class AIShipCommandDataAuthoringComponent : MonoBehaviour
{
    public class Baker : Baker<AIShipCommandDataAuthoringComponent>
    {
        public override void Bake(AIShipCommandDataAuthoringComponent authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<AIShipCommandData>(entity);
        }
    }
}
using Unity.Entities;
using UnityEngine;

public class AIShipTagAuthoringComponent : MonoBehaviour
{
    public class Baker : Baker<AIShipTagAuthoringComponent>
    {
        public override void Bake(AIShipTagAuthoringComponent authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new AIShipTagComponentData());
        }
    }
}
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Asteroids.Mixed
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct AISteeringSystem : ISystem
    {
        private EntityQuery m_PlayerShipQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LevelComponent>();
            state.RequireForUpdate<AsteroidsSpawner>();

            // Query all player-controlled ships (exclude AI)
            m_PlayerShipQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(LocalTransform), typeof(ShipStateComponentData) },
                None = new ComponentType[] { typeof(AIShipTagComponentData) }
            });
        }

        [BurstCompile]
        private partial struct SteeringJob : IJobEntity
        {
            [ReadOnly] public NativeArray<LocalTransform> playerShipTransforms;

            public float deltaTime;

            public float shipTurnRate;

            public void Execute(ref LocalTransform transform, in AIShipTagComponentData tag)
            {
                if (playerShipTransforms.Length == 0)
                {
                    return;
                }

                var myPos = transform.Position;
                var closestTarget = playerShipTransforms[0].Position;
                var minDistanceSq = math.distancesq(myPos, closestTarget);

                for (var i = 1; i < playerShipTransforms.Length; i++)
                {
                    var pos = playerShipTransforms[i].Position;
                    var distSq = math.distancesq(myPos, pos);
                    if (distSq < minDistanceSq)
                    {
                        minDistanceSq = distSq;
                        closestTarget = pos;
                    }
                }

                var toTarget = math.normalize(closestTarget - myPos);
                var currentForward = math.mul(transform.Rotation, new float3(0, -1, 0));

                // Compute rotation angle
                var angle = math.atan2(toTarget.x, toTarget.y) - math.atan2(currentForward.x, currentForward.y);

                // Normalize angle to [-π, π]
                angle = math.atan2(math.sin(angle), math.cos(angle));

                // Limit rotation per frame by turn rate
                var maxRotation = math.radians(shipTurnRate) * deltaTime;
                angle = math.clamp(angle, -maxRotation, maxRotation);

                transform.Rotation = math.mul(transform.Rotation, quaternion.RotateZ(angle));
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var playerShipTransforms = m_PlayerShipQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);

            var level = SystemAPI.GetSingleton<LevelComponent>();

            var job = new SteeringJob
            {
                playerShipTransforms = playerShipTransforms,
                deltaTime = SystemAPI.Time.DeltaTime,
                shipTurnRate = level.shipRotationRate
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }
    }
}
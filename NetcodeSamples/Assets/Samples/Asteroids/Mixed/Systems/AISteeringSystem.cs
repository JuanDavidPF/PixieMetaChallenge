using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Asteroids.Mixed
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct AISteeringSystem : ISystem
    {
        private EntityQuery m_PlayerShipQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<LevelComponent>();
            state.RequireForUpdate<AsteroidsSpawner>();

            // Query all player-controlled ships (exclude AI)
            m_PlayerShipQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(LocalTransform), typeof(ShipStateComponentData) },
                None = new ComponentType[] { typeof(AIShipCommandData) }
            });
        }

        [BurstCompile]
        private partial struct LocateTargetJob : IJobEntity
        {
            [ReadOnly] public NativeArray<LocalTransform> playerShipTransforms;

            [ReadOnly] public NativeArray<LocalTransform> asteroidTransforms;

            public float detectionRadiusSq;

            public float preferredDistanceSq;

            public void Execute(ref AIShipCommandData aiCommand, in LocalTransform transform)
            {
                var myPos = transform.Position;
                float3? targetPos = null;
                var minDistSq = float.MaxValue;

                // Look for player ships within detection radius
                for (var i = 0; i < playerShipTransforms.Length; i++)
                {
                    var pos = playerShipTransforms[i].Position;
                    var distSq = math.distancesq(myPos, pos);
                    if (distSq < detectionRadiusSq && distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        targetPos = pos;
                    }
                }

                // If no player found, look for closest asteroid
                if (!targetPos.HasValue && asteroidTransforms.Length > 0)
                {
                    for (var i = 0; i < asteroidTransforms.Length; i++)
                    {
                        var pos = asteroidTransforms[i].Position;
                        var distSq = math.distancesq(myPos, pos);
                        if (distSq < minDistSq)
                        {
                            minDistSq = distSq;
                            targetPos = pos;
                        }
                    }
                }

                if (!targetPos.HasValue)
                {
                    aiCommand = default;
                    return;
                }

                var toTarget = math.normalize(targetPos.Value - myPos);
                var forward = math.mul(transform.Rotation, new float3(0, -1, 0));
                var angle = math.atan2(toTarget.x, toTarget.y) - math.atan2(forward.x, forward.y);
                angle = math.atan2(math.sin(angle), math.cos(angle));

                aiCommand.left = (byte)(angle > 0.01f ? 1 : 0);
                aiCommand.right = (byte)(angle < -0.01f ? 1 : 0);
                aiCommand.thrust = (byte)(minDistSq > preferredDistanceSq ? 1 : 0);
                aiCommand.shoot = (byte)(math.abs(angle) > 3 ? 1 : 0);
            }
        }

        [WithAll(typeof(AIShipCommandData), typeof(Simulate))]
        [BurstCompile]
        private partial struct ApplyAISteeringJob : IJobEntity
        {
            public LevelComponent level;

            public EntityCommandBuffer.ParallelWriter commandBuffer;

            public Entity bulletPrefab;

            public float deltaTime;

            public NetworkTick currentTick;

            public void Execute(Entity entity, [EntityIndexInQuery] int entityIndexInQuery,
                ref LocalTransform transform, ref Velocity velocity,
                ref ShipStateComponentData state,
                in AIShipCommandData inputData,
                in GhostOwner ghostOwner)
            {
                state.State = inputData.thrust;

                if (inputData.left == 1)
                {
                    transform.Rotation = math.mul(transform.Rotation, quaternion.RotateZ(math.radians(level.shipRotationRate * deltaTime)));
                }

                if (inputData.right == 1)
                {
                    transform.Rotation = math.mul(transform.Rotation, quaternion.RotateZ(math.radians(-level.shipRotationRate * deltaTime)));
                }

                if (inputData.thrust == 1)
                {
                    var fwd = new float3(0, level.shipForwardForce * deltaTime, 0);
                    velocity.Value += math.mul(transform.Rotation, fwd).xy;
                }

                transform.Position.xy += velocity.Value * deltaTime;

                var canShoot = !state.WeaponCooldown.IsValid || currentTick.IsNewerThan(state.WeaponCooldown);
                if (inputData.shoot != 0 && canShoot)
                {
                    if (bulletPrefab != Entity.Null)
                    {
                        var e = commandBuffer.Instantiate(entityIndexInQuery, bulletPrefab);
                        var bulletTx = transform;
                        bulletTx.Scale = 10;
                        commandBuffer.SetComponent(entityIndexInQuery, e, bulletTx);
                        var vel = new Velocity { Value = math.mul(transform.Rotation, new float3(0, level.bulletVelocity, 0)).xy };
                        commandBuffer.SetComponent(entityIndexInQuery, e, new GhostOwner { NetworkId = ghostOwner.NetworkId });
                        commandBuffer.SetComponent(entityIndexInQuery, e, vel);
                    }

                    state.WeaponCooldown = currentTick;
                    state.WeaponCooldown.Add(level.bulletRofCooldownTicks);
                }
                else if (canShoot)
                {
                    state.WeaponCooldown = NetworkTick.Invalid;
                }
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            var playerShipTransforms = m_PlayerShipQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);

            // Add asteroid query
            var asteroidQuery = state.GetEntityQuery(ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<AsteroidTagComponentData>());
            var asteroidTransforms = asteroidQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);

            var level = SystemAPI.GetSingleton<LevelComponent>();
            float detectionRadiusSq = level.relevancyRadius * level.relevancyRadius;
            var preferredDistanceSq = 200f * 200f;

            var locateTargetJob = new LocateTargetJob
            {
                playerShipTransforms = playerShipTransforms,
                asteroidTransforms = asteroidTransforms,
                detectionRadiusSq = detectionRadiusSq,
                preferredDistanceSq = preferredDistanceSq
            };
            state.Dependency = locateTargetJob.ScheduleParallel(state.Dependency);

            var applyAISteering = new ApplyAISteeringJob
            {
                level = level,
                commandBuffer = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
                bulletPrefab = SystemAPI.GetSingleton<AsteroidsSpawner>().Bullet,
                deltaTime = SystemAPI.Time.DeltaTime,
                currentTick = networkTime.ServerTick
            };

            state.Dependency = applyAISteering.ScheduleParallel(state.Dependency);
        }
    }
}
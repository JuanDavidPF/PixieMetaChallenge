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
        private partial struct LocatePlayerJob : IJobEntity
        {
            [ReadOnly] public NativeArray<LocalTransform> playerShipTransforms;

            public float deltaTime;

            public float shipTurnRate;

            public void Execute(
                ref AIShipCommandData aiCommand,
                in LocalTransform transform)
            {
                if (playerShipTransforms.Length == 0)
                {
                    aiCommand.left = 0;
                    aiCommand.right = 0;
                    aiCommand.thrust = 0;
                    return;
                }

                var myPos = transform.Position;
                var closestPos = playerShipTransforms[0].Position;
                var minDistSq = math.distancesq(myPos, closestPos);

                for (var i = 1; i < playerShipTransforms.Length; i++)
                {
                    var pos = playerShipTransforms[i].Position;
                    var distSq = math.distancesq(myPos, pos);
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        closestPos = pos;
                    }
                }

                var toTarget = math.normalize(closestPos - myPos);

                // Forward is Y- if your ship is "pointing up"
                var forward = math.mul(transform.Rotation, new float3(0, -1, 0));

                var angle = math.atan2(toTarget.x, toTarget.y) - math.atan2(forward.x, forward.y);
                angle = math.atan2(math.sin(angle), math.cos(angle)); // normalize to [-π, π]

                var maxRotation = math.radians(shipTurnRate) * deltaTime;

                // Set command flags based on desired angle
                aiCommand.left = (byte)(angle > 0.01f ? 1 : 0);
                aiCommand.right = (byte)(angle < -0.01f ? 1 : 0);
                aiCommand.thrust = 1; // Always move forward (you can add smarter logic)
                aiCommand.shoot = 0;
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

            public byte isFirstFullTick;

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
                    if (bulletPrefab != Entity.Null && isFirstFullTick == 1)
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
            var playerShipTransforms = m_PlayerShipQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();

            var level = SystemAPI.GetSingleton<LevelComponent>();

            var locatePlayerJob = new LocatePlayerJob
            {
                playerShipTransforms = playerShipTransforms,
                deltaTime = SystemAPI.Time.DeltaTime,
                shipTurnRate = level.shipRotationRate
            };

            state.Dependency = locatePlayerJob.ScheduleParallel(state.Dependency);

            var applyAISteering = new ApplyAISteeringJob
            {
                level = SystemAPI.GetSingleton<LevelComponent>(),
                commandBuffer = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
                bulletPrefab = SystemAPI.GetSingleton<AsteroidsSpawner>().Bullet,
                deltaTime = SystemAPI.Time.DeltaTime,
                currentTick = networkTime.ServerTick,
                isFirstFullTick = (byte)(networkTime.IsFirstTimeFullyPredictingTick ? 1 : 0)
            };

            state.Dependency = applyAISteering.ScheduleParallel(state.Dependency);
            
        }
    }
}
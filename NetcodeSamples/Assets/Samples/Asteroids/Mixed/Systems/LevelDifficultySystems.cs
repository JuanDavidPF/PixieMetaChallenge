using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Asteroids.Mixed
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct LevelDifficultySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LevelComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var level = SystemAPI.GetSingletonRW<LevelComponent>();

            level.ValueRW.elapsedTime += SystemAPI.Time.DeltaTime;

            // AI spawn count over time
            var aiCount = level.ValueRO.initialAISpawnCount + (int)(level.ValueRO.elapsedTime * level.ValueRO.aiSpawnIncreaseRatePerSecond);
            level.ValueRW.numAsteroids = math.clamp(aiCount, level.ValueRO.initialAISpawnCount, level.ValueRO.maxAISpawnCount);

            // Cooldown reduction
            var newCooldown = level.ValueRW.maxBulletCooldownTicks - level.ValueRO.elapsedTime * level.ValueRO.bulletCooldownDecreaseRatePerSecond;
            level.ValueRW.bulletRofCooldownTicks = (uint)math.max(level.ValueRO.minBulletCooldownTicks, newCooldown);

            // Detection radius increase
            var newRadius = level.ValueRO.initialDetectionRadius + level.ValueRO.elapsedTime * level.ValueRO.detectionRadiusIncreaseRatePerSecond;
            level.ValueRW.relevancyRadius = (int)math.clamp(newRadius, level.ValueRO.initialDetectionRadius, level.ValueRO.maxDetectionRadius);
        }
    }
}
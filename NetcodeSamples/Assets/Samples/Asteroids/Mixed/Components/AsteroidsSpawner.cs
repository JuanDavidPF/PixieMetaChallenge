using Unity.Entities;

public struct AsteroidsSpawner : IComponentData
{
    public Entity AIShip;
    public Entity Ship;
    public Entity Bullet;
    public Entity Asteroid;
    public Entity StaticAsteroid;
}
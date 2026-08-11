namespace Wayfu.Lamkn
{
    // Các loại âm thanh gameplay. Dùng cho BaseObject.GetHitAudioType và hệ thống phát tiếng va chạm.
    public enum AudioType
    {
        None = 0,
        UIClick = 1,
        MenuMusic = 2,
        GameplayMusic = 3,
        CannonShoot = 4,
        CanHit = 5,
        CanBreak = 6,
        StoneHit = 7,
        StoneBreak = 8,
        WormholeHit = 9,
        WormholeBreak = 10,
        BlockerHit = 11,
        Win = 12,
        Lose = 13,
        CartArrive = 14,
        CartDepart = 15,
        CoinAppear = 16,
        CoinHit = 17,
        JarBreak = 18,
        BoxHit = 19,
        BoxBreak = 20,
        IceHit = 21,
        IceBreak = 22,
        TntExplode = 23,
        BoxCylinderHit = 24,
        BoxCylinderBreak = 25,
        RocketIntro = 26,
        RocketFlight = 27,
        RocketHit = 28,
        AnimalHit = 29,
        AnimalGround = 30,
    }
}

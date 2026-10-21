namespace Th01.Core.Game;

public enum ItemFlag : byte
{
    Free = 0,
    Splash = 1,
    Fall = 2,
    Bounce = 3,
    Collected = 99,
    CollectedOverCap = 100,
}

/// <summary>Bomb and point items dropped by cards. ref: th01/main/stage/item.cpp</summary>
public sealed class Items
{
    public const int BombCount = 4;
    public const int PointCount = 10;
    const int ItemW = 32;
    const int ItemH = 32;
    const int PointCap = 65530;

    public sealed class Slot
    {
        public int Left, Top, VelocityY;
        public ItemFlag Flag;
        /// <summary>splash_radius during Splash, collect_time once collected (union in the original).</summary>
        public byte State;
    }

    readonly GameWorld _w;
    public readonly Slot[] Bombs = new Slot[BombCount];
    public readonly Slot[] Points = new Slot[PointCount];

    public Items(GameWorld w)
    {
        _w = w;
        for (int i = 0; i < BombCount; i++) Bombs[i] = new Slot();
        for (int i = 0; i < PointCount; i++) Points[i] = new Slot();
    }

    public void Reset()
    {
        foreach (var s in Bombs) s.Flag = ItemFlag.Free;
        foreach (var s in Points) s.Flag = ItemFlag.Free;
    }

    static void Add(Slot[] slots, int left, int top)
    {
        foreach (var s in slots)
        {
            if (s.Flag != ItemFlag.Free) continue;
            s.Flag = ItemFlag.Splash;
            s.Left = left;
            s.Top = top;
            s.VelocityY = 2;
            s.State = 4;
            return;
        }
    }

    public void AddBomb(int left, int top) => Add(Bombs, left, top);
    public void AddPoint(int left, int top) => Add(Points, left, top);

    static bool IsVisible(Slot s) => s.Flag > ItemFlag.Splash && s.Flag < ItemFlag.Collected;

    // Symmetric around the player's centre, whatever it looks like on screen.
    bool PlayerHit(Slot s) =>
        s.Left > _w.Player.Left - (Playfield.PlayerW - ItemW / 4) &&
        s.Left < _w.Player.Left + (Playfield.PlayerW - ItemW / 4) &&
        s.Top > Player.Top - (Playfield.PlayerH - ItemH / 4) &&
        IsVisible(s);

    static void CollectInit(Slot s)
    {
        s.VelocityY = -2;
        s.State = 16;
        s.Top = s.Top >= Playfield.Bottom - ItemH ? Playfield.Bottom - ItemH : s.Top;
    }

    void BombHitTest(Slot s)
    {
        if (!PlayerHit(s)) return;
        CollectInit(s);
        if (_w.RemBombs < Playfield.BombsMax)
        {
            _w.RemBombs++;
            s.Flag = ItemFlag.Collected;
        }
        else
        {
            _w.Score += 10000;
            s.Flag = ItemFlag.CollectedOverCap;
        }
        _w.Sound.PlaySe(15);
    }

    void PointHitTest(Slot s)
    {
        if (!PlayerHit(s)) return;
        CollectInit(s);
        // 16-bit counter: this is how the original avoids overflow.
        if (_w.PointValue < 59999) _w.PointValue += (ushort)(_w.PointValue < 10000 ? 1000 : 10000);
        if (_w.PointValue >= 60000) _w.PointValue = PointCap;
        _w.Score += _w.PointValue;
        s.Flag = ItemFlag.CollectedOverCap;
        _w.Sound.PlaySe(15);
    }

    /// <summary>items_unput_update_render(): bombs first, then points.</summary>
    public void Update()
    {
        foreach (var s in Bombs)
            if (s.Flag != ItemFlag.Free) UpdateOne(s, BombHitTest, dropPoint: false);
        foreach (var s in Points)
            if (s.Flag != ItemFlag.Free) UpdateOne(s, PointHitTest, dropPoint: true);
    }

    void UpdateOne(Slot s, Action<Slot> hitTest, bool dropPoint)
    {
        hitTest(s);
        if (s.Flag == ItemFlag.Splash)
        {
            s.State += 4;
            if (s.State >= 48) s.Flag = ItemFlag.Fall;
        }
        else if (s.Flag == ItemFlag.Fall)
        {
            s.Top += s.VelocityY;
            if (s.Top >= Playfield.Bottom - ItemH)
            {
                s.VelocityY = -8;
                s.Flag = ItemFlag.Bounce;
                Bounce(s, dropPoint);
            }
        }
        else if (s.Flag == ItemFlag.Bounce)
        {
            Bounce(s, dropPoint);
        }
        else if (s.Flag >= ItemFlag.Collected)
        {
            s.Top += s.VelocityY;
            s.State--;
            if (s.State == 0) s.Flag = ItemFlag.Free;
        }
    }

    void Bounce(Slot s, bool dropPoint)
    {
        s.Top += s.VelocityY;
        s.VelocityY++;
        if (s.Top >= Playfield.Bottom)
        {
            s.Flag = ItemFlag.Free;
            if (dropPoint) _w.PointValue = 0; // an uncollected point item resets the chain
        }
    }

    public void Hash(ref StateHasher h)
    {
        foreach (var s in Bombs) HashSlot(ref h, s);
        foreach (var s in Points) HashSlot(ref h, s);
    }

    static void HashSlot(ref StateHasher h, Slot s)
    {
        h.Add((int)s.Flag); h.Add(s.Left); h.Add(s.Top); h.Add(s.VelocityY); h.Add(s.State);
    }
}

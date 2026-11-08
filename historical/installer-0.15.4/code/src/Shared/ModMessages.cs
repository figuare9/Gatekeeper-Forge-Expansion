namespace Gatekeeper.Mods;

public static class ModProtocol
{
    public const int MaxMessageChars = 2048;
    public const int QuoteBatch = 8;
}

public sealed class ExchangeQuote
{
    public int Item { get; set; }
    public int Native { get; set; } = -1;
    public int Price { get; set; } = -1;
    public int Currency { get; set; }
    public int Sale { get; set; } = -1;
}
public sealed class ExchangeMessage
{
    public string Kind { get; set; } = "";
    public string Visit { get; set; } = "";
    public string Id { get; set; } = "";
    public int Item { get; set; }
    public int Price { get; set; }
    public int Native { get; set; }
    public int Spent { get; set; }
    public int Accumulated { get; set; }
    public int Count { get; set; }
    public int Character { get; set; }
    public int[]? Items { get; set; }
    public ExchangeQuote[]? Quotes { get; set; }
    public string Note { get; set; } = "";
}

public sealed class SlotMessage
{
    public int Version { get; set; } = SlotRules.Protocol;
    public int Scene { get; set; }
    public string Kind { get; set; } = "";
    public string Session { get; set; } = "";
    public string Visit { get; set; } = "";
    public string Id { get; set; } = "";
    public int Owner { get; set; }
    public int Stake { get; set; }
    public int Payout { get; set; }
    public int Accumulated { get; set; }
    public int Spent { get; set; }
    public bool Won { get; set; }
    public bool WalletSync { get; set; }
    public float Elapsed { get; set; }
    public int Delta { get; set; }
    public int Character { get; set; }
    public string Change { get; set; } = "";
    public string Note { get; set; } = "";
    public float[]? Pose { get; set; }
    public float[]? ShopPose { get; set; }
    public string ShopVisit { get; set; } = "";
    public ExchangeMessage? Shop { get; set; }
    public bool CannonUnlocked { get; set; }
    public bool CannonEnabled { get; set; }
    public CannonMessage? Cannon { get; set; }
    public ForgeMessage? Forge {get;set;}
    public string ForgeRevision {get;set;}="";
    public bool ForgeEnabled {get;set;}
    public ForgeUnlockMessage? ForgeUnlock {get;set;}
}

public sealed class CannonMessage
{
    public string Kind { get; set; }="";
    public string Round { get; set; }="";
    public string World { get; set; }="";
    public int Owner { get; set; }
    public int Character { get; set; }
    public int Scene { get; set; }
    public long Sequence { get; set; }
    public int Stacks { get; set; }
    public int Unit { get; set; }
    public int Warp { get; set; }
    public float Yaw { get; set; }
    public float[]? Velocity { get; set; }
    public float Damage { get; set; }
    public float Flight { get; set; }
    public float[]? Position { get; set; }
    public float[]? Aim { get; set; }
    public int[]? Targets { get; set; }
}


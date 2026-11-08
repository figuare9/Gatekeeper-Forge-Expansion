namespace Gatekeeper.Mods;

public static class CustomModRules
{
    public static bool IsRestAreaScene(int scene) => scene is 20 or 998;
    public static bool CanBuy(bool inQuietBay, bool authorizedSession, bool unlocked, bool dlcAvailable, bool alive,
        bool atStackLimit, int price, int balance, bool pending) =>
        inQuietBay && authorizedSession && unlocked && dlcAvailable && alive && !atStackLimit && price >= 0 && balance >= price && !pending;

}

// Ordinary shops display the client currency manager. A server SyncVar alone is
// insufficient: the owning client's reactive spent counter must also change.
public readonly record struct ShopFunds(int Displayed, int Held, int ClientSpent, int ServerSpent)
{
    public bool Agrees => Displayed >= 0 && Displayed == Held && ClientSpent == ServerSpent;
}

public sealed record ShopPayment(ShopFunds Before, int Price)
{
    public bool CanStart => Price >= 0 && Before.Agrees && Before.Displayed >= Price &&
        (long)Before.ClientSpent + Price <= int.MaxValue;
    public bool Charged(ShopFunds now) => CanStart && now.Agrees &&
        (long)now.ClientSpent - Before.ClientSpent == Price;
    public bool Refunded(ShopFunds now) => now.Agrees && now.ClientSpent == Before.ClientSpent;
    public int RefundAmount(ShopFunds now)
    {
        var delta = (long)now.ClientSpent - Before.ClientSpent;
        return delta >= 0 && delta <= Price ? (int)delta : -1;
    }
}
